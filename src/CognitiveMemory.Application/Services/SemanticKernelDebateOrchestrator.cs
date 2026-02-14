using System.Text.Json;
using CognitiveMemory.Application.AI;
using CognitiveMemory.Application.AI.Plugins;
using CognitiveMemory.Application.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;

namespace CognitiveMemory.Application.Services;

public sealed class SemanticKernelDebateOrchestrator(
    IMemoryKernelFactory kernelFactory,
    ILogger<SemanticKernelDebateOrchestrator> logger,
    MemoryRecallPlugin? memoryRecallPlugin = null,
    MemoryWritePlugin? memoryWritePlugin = null,
    MemoryGovernancePlugin? memoryGovernancePlugin = null,
    GroundingPlugin? groundingPlugin = null) : IDebateOrchestrator
{
    private const int AgentTimeoutSeconds = 20;

    public async Task<DebateResult> OrchestrateAsync(string question, QueryClaimsResponse memoryPacket, CancellationToken cancellationToken)
    {
        if (memoryPacket.Claims.Count == 0)
        {
            return new DebateResult
            {
                Answer = "I do not have enough evidence to answer this confidently.",
                Confidence = 0,
                Citations = [],
                UncertaintyFlags = ["InsufficientEvidence"],
                Contradictions = []
            };
        }

        var top = memoryPacket.Claims[0];
        var fallback = BuildFallbackGenerator(top, memoryPacket.Meta.UncertaintyFlags);

        try
        {
            var kernel = kernelFactory.CreateKernel();
            if (memoryRecallPlugin is not null)
            {
                kernel.Plugins.AddFromObject(memoryRecallPlugin, "memory_recall");
            }

            if (memoryWritePlugin is not null)
            {
                kernel.Plugins.AddFromObject(memoryWritePlugin, "memory_write");
            }

            if (memoryGovernancePlugin is not null)
            {
                kernel.Plugins.AddFromObject(memoryGovernancePlugin, "memory_governance");
            }

            if (groundingPlugin is not null)
            {
                kernel.Plugins.AddFromObject(groundingPlugin, "grounding");
            }

            var generatorRaw = await InvokeAgentWithTimeoutAsync(
                kernel,
                "Generator",
                BuildGeneratorInstructions(),
                BuildGeneratorInput(question, memoryPacket),
                cancellationToken);

            var generator = TryParseGenerator(generatorRaw) ?? fallback;

            var skepticRaw = await InvokeAgentWithTimeoutAsync(
                kernel,
                "Skeptic",
                BuildSkepticInstructions(),
                BuildSkepticInput(generator, memoryPacket),
                cancellationToken);

            var skeptic = TryParseSkeptic(skepticRaw) ?? RunSkeptic(generator, memoryPacket);

            var librarianRaw = await InvokeAgentWithTimeoutAsync(
                kernel,
                "Librarian",
                BuildLibrarianInstructions(),
                BuildLibrarianInput(generator, skeptic, memoryPacket),
                cancellationToken);

            var librarian = TryParseLibrarian(librarianRaw) ?? RunLibrarian(generator, memoryPacket, skeptic);

            var judgeRaw = await InvokeAgentWithTimeoutAsync(
                kernel,
                "Judge",
                BuildJudgeInstructions(),
                BuildJudgeInput(generator, skeptic, librarian, memoryPacket),
                cancellationToken);

            var judged = TryParseJudge(judgeRaw, top.Contradictions);
            if (judged is not null)
            {
                return judged;
            }

            return RunJudge(generator, skeptic, librarian, memoryPacket);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Agent orchestration failed. Falling back to deterministic debate.");
            var skeptic = RunSkeptic(fallback, memoryPacket);
            var librarian = RunLibrarian(fallback, memoryPacket, skeptic);
            return RunJudge(fallback, skeptic, librarian, memoryPacket);
        }
    }

    private async Task<string> InvokeAgentWithTimeoutAsync(
        Kernel kernel,
        string name,
        string instructions,
        string input,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(AgentTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            var agent = new ChatCompletionAgent
            {
                Name = name,
                Instructions = instructions,
                Kernel = kernel
            };

            var message = new ChatMessageContent(AuthorRole.User, input);
            var latest = string.Empty;

            await foreach (var response in agent.InvokeAsync(message).WithCancellation(linkedCts.Token))
            {
                var text = ExtractAgentText(response);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    latest = text;
                }
            }

            return latest;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            logger.LogWarning("Agent {AgentName} timed out after {TimeoutSeconds}s.", name, AgentTimeoutSeconds);
            return string.Empty;
        }
    }

    private static string BuildGeneratorInstructions()
        => PromptLoader.LoadText(PromptCatalog.DebateGeneratorPromptPath);

    private static string BuildSkepticInstructions()
        => PromptLoader.LoadText(PromptCatalog.DebateSkepticPromptPath);

    private static string BuildLibrarianInstructions()
        => PromptLoader.LoadText(PromptCatalog.DebateLibrarianPromptPath);

    private static string BuildJudgeInstructions()
        => PromptLoader.LoadText(PromptCatalog.DebateJudgePromptPath);

    private static string BuildGeneratorInput(string question, QueryClaimsResponse memoryPacket)
        => $"Question:\n{question}\n\nMemoryPacket(JSON):\n{JsonSerializer.Serialize(memoryPacket)}";

    private static string BuildSkepticInput(GeneratorOutput generator, QueryClaimsResponse memoryPacket)
        => $"Generator(JSON):\n{JsonSerializer.Serialize(generator)}\n\nMemoryPacket(JSON):\n{JsonSerializer.Serialize(memoryPacket)}";

    private static string BuildLibrarianInput(GeneratorOutput generator, SkepticOutput skeptic, QueryClaimsResponse memoryPacket)
        => $"Generator(JSON):\n{JsonSerializer.Serialize(generator)}\n\nSkeptic(JSON):\n{JsonSerializer.Serialize(skeptic)}\n\nMemoryPacket(JSON):\n{JsonSerializer.Serialize(memoryPacket)}";

    private static string BuildJudgeInput(GeneratorOutput generator, SkepticOutput skeptic, LibrarianOutput librarian, QueryClaimsResponse memoryPacket)
        => $"Generator(JSON):\n{JsonSerializer.Serialize(generator)}\n\nSkeptic(JSON):\n{JsonSerializer.Serialize(skeptic)}\n\nLibrarian(JSON):\n{JsonSerializer.Serialize(librarian)}\n\nMemoryPacket(JSON):\n{JsonSerializer.Serialize(memoryPacket)}";

    private static string? ExtractAgentText(object? response)
    {
        if (response is null)
        {
            return null;
        }

        if (response is ChatMessageContent messageContent)
        {
            return messageContent.Content;
        }

        var responseType = response.GetType();
        var messageProperty = responseType.GetProperty("Message");
        var messageValue = messageProperty?.GetValue(response);
        if (messageValue is ChatMessageContent chatMessageContent)
        {
            return chatMessageContent.Content;
        }

        var contentProperty = responseType.GetProperty("Content");
        var contentValue = contentProperty?.GetValue(response) as string;
        if (!string.IsNullOrWhiteSpace(contentValue))
        {
            return contentValue;
        }

        return response.ToString();
    }

    private static SkepticOutput RunSkeptic(GeneratorOutput generator, QueryClaimsResponse memoryPacket)
    {
        var critiques = new List<string>();
        if (generator.Citations.Count == 0)
        {
            critiques.Add("MissingCitation");
        }

        var top = memoryPacket.Claims[0];
        if (top.Contradictions.Any(c => string.Equals(c.Status, "Open", StringComparison.OrdinalIgnoreCase)))
        {
            critiques.Add("UnresolvedContradiction");
        }

        if (generator.Confidence > 0.85 && top.Score < 0.75)
        {
            critiques.Add("OverconfidentLanguage");
        }

        return new SkepticOutput
        {
            Critiques = critiques,
            SuggestedConfidence = critiques.Count > 0 ? Math.Min(generator.Confidence, 0.72) : generator.Confidence
        };
    }

    private static LibrarianOutput RunLibrarian(GeneratorOutput generator, QueryClaimsResponse memoryPacket, SkepticOutput skeptic)
    {
        if (!skeptic.Critiques.Contains("MissingCitation"))
        {
            return new LibrarianOutput
            {
                Citations = generator.Citations,
                Notes = []
            };
        }

        var fallbackCitations = memoryPacket.Claims
            .SelectMany(claim => claim.Evidence.Select(evidence => new AnswerCitation
            {
                ClaimId = claim.ClaimId,
                EvidenceId = evidence.EvidenceId
            }))
            .Take(2)
            .ToList();

        return new LibrarianOutput
        {
            Citations = fallbackCitations,
            Notes = ["RecoveredCitationsFromMemoryPacket"]
        };
    }

    private static DebateResult RunJudge(
        GeneratorOutput generator,
        SkepticOutput skeptic,
        LibrarianOutput librarian,
        QueryClaimsResponse memoryPacket)
    {
        var top = memoryPacket.Claims[0];
        var hasSevereContradiction = top.Contradictions.Any(c =>
            string.Equals(c.Status, "Open", StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(c.Severity, "High", StringComparison.OrdinalIgnoreCase) || string.Equals(c.Severity, "Critical", StringComparison.OrdinalIgnoreCase)));

        var confidence = skeptic.SuggestedConfidence;
        var flags = memoryPacket.Meta.UncertaintyFlags.ToList();
        if (hasSevereContradiction)
        {
            if (!flags.Contains("SevereContradiction"))
            {
                flags.Add("SevereContradiction");
            }

            confidence = Math.Min(confidence, 0.65);
        }

        var citations = librarian.Citations;
        var answer = generator.Answer;
        if (citations.Count == 0)
        {
            answer = "I do not have enough cited evidence to provide a factual answer.";
            confidence = 0;
            flags.Add("InsufficientEvidence");
        }
        else if (string.IsNullOrWhiteSpace(answer))
        {
            answer = "Based on available evidence, I cannot provide a confident answer yet.";
            flags.Add("InsufficientEvidence");
            confidence = Math.Min(confidence, 0.5);
        }

        if (confidence < 0.7)
        {
            answer = SofteningPrefix(answer);
        }

        return new DebateResult
        {
            Answer = answer,
            Confidence = Math.Clamp(confidence, 0, 1),
            Citations = citations,
            UncertaintyFlags = flags.Distinct().ToList(),
            Contradictions = top.Contradictions
        };
    }

    private static GeneratorOutput BuildFallbackGenerator(QueryClaimItem top, IReadOnlyList<string> uncertainty)
    {
        var answer = $"Based on recorded evidence, the best-supported value for '{top.Predicate}' is '{top.LiteralValue}'.";
        var citations = top.Evidence
            .Select(e => new AnswerCitation { ClaimId = top.ClaimId, EvidenceId = e.EvidenceId })
            .Take(2)
            .ToList();

        var confidence = Math.Clamp(top.Confidence, 0, 1);
        if (uncertainty.Count > 0)
        {
            confidence = Math.Min(confidence, 0.75);
        }

        return new GeneratorOutput
        {
            Answer = answer,
            Confidence = confidence,
            Citations = citations
        };
    }

    private static string SofteningPrefix(string answer)
    {
        const string prefix = "Based on available evidence, it appears that ";
        if (string.IsNullOrWhiteSpace(answer))
        {
            return "Based on available evidence, it appears that the answer is uncertain.";
        }

        if (answer.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return answer;
        }

        return prefix + char.ToLowerInvariant(answer[0]) + answer[1..];
    }

    private static GeneratorOutput? TryParseGenerator(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (!root.TryGetProperty("answer", out var answerEl) || answerEl.ValueKind != JsonValueKind.String)
            {
                return null;
            }
            var answer = answerEl.GetString();
            if (string.IsNullOrWhiteSpace(answer))
            {
                return null;
            }

            var confidence = 0.5;
            if (root.TryGetProperty("confidence", out var confEl) && confEl.TryGetDouble(out var parsedConfidence))
            {
                confidence = Math.Clamp(parsedConfidence, 0, 1);
            }

            var citations = ParseCitations(root);

            return new GeneratorOutput
            {
                Answer = answerEl.GetString() ?? string.Empty,
                Confidence = confidence,
                Citations = citations
            };
        }
        catch
        {
            return null;
        }
    }

    private static SkepticOutput? TryParseSkeptic(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            var critiques = new List<string>();
            if (root.TryGetProperty("critiques", out var critiquesEl) && critiquesEl.ValueKind == JsonValueKind.Array)
            {
                critiques.AddRange(critiquesEl.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString())
                    .Where(x => !string.IsNullOrWhiteSpace(x))!
                    .Cast<string>());
            }

            var suggestedConfidence = 0.5;
            if (root.TryGetProperty("suggestedConfidence", out var confEl) && confEl.TryGetDouble(out var conf))
            {
                suggestedConfidence = Math.Clamp(conf, 0, 1);
            }

            return new SkepticOutput
            {
                Critiques = critiques,
                SuggestedConfidence = suggestedConfidence
            };
        }
        catch
        {
            return null;
        }
    }

    private static LibrarianOutput? TryParseLibrarian(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            var citations = ParseCitations(root);
            var notes = new List<string>();
            if (root.TryGetProperty("notes", out var notesEl) && notesEl.ValueKind == JsonValueKind.Array)
            {
                notes.AddRange(notesEl.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString())
                    .Where(x => !string.IsNullOrWhiteSpace(x))!
                    .Cast<string>());
            }

            return new LibrarianOutput
            {
                Citations = citations,
                Notes = notes
            };
        }
        catch
        {
            return null;
        }
    }

    private static DebateResult? TryParseJudge(string? raw, IReadOnlyList<QueryContradictionItem> contradictions)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (!root.TryGetProperty("answer", out var answerEl) || answerEl.ValueKind != JsonValueKind.String)
            {
                return null;
            }
            var answer = answerEl.GetString();
            if (string.IsNullOrWhiteSpace(answer))
            {
                return null;
            }

            var confidence = 0.5;
            if (root.TryGetProperty("confidence", out var confEl) && confEl.TryGetDouble(out var conf))
            {
                confidence = Math.Clamp(conf, 0, 1);
            }

            var flags = new List<string>();
            if (root.TryGetProperty("uncertaintyFlags", out var flagsEl) && flagsEl.ValueKind == JsonValueKind.Array)
            {
                flags.AddRange(flagsEl.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString())
                    .Where(x => !string.IsNullOrWhiteSpace(x))!
                    .Cast<string>());
            }

            return new DebateResult
            {
                Answer = answerEl.GetString() ?? string.Empty,
                Confidence = confidence,
                Citations = ParseCitations(root),
                UncertaintyFlags = flags.Distinct().ToList(),
                Contradictions = contradictions
            };
        }
        catch
        {
            return null;
        }
    }

    private static List<AnswerCitation> ParseCitations(JsonElement root)
    {
        var citations = new List<AnswerCitation>();
        if (!root.TryGetProperty("citations", out var citationsEl) || citationsEl.ValueKind != JsonValueKind.Array)
        {
            return citations;
        }

        foreach (var citation in citationsEl.EnumerateArray())
        {
            if (!citation.TryGetProperty("claimId", out var claimIdEl) || claimIdEl.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            if (!citation.TryGetProperty("evidenceId", out var evidenceIdEl) || evidenceIdEl.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            if (!Guid.TryParse(claimIdEl.GetString(), out var claimId) || !Guid.TryParse(evidenceIdEl.GetString(), out var evidenceId))
            {
                continue;
            }

            citations.Add(new AnswerCitation { ClaimId = claimId, EvidenceId = evidenceId });
        }

        return citations;
    }

    private sealed class GeneratorOutput
    {
        public string Answer { get; init; } = string.Empty;
        public double Confidence { get; init; }
        public IReadOnlyList<AnswerCitation> Citations { get; init; } = [];
    }

    private sealed class SkepticOutput
    {
        public IReadOnlyList<string> Critiques { get; init; } = [];
        public double SuggestedConfidence { get; init; }
    }

    private sealed class LibrarianOutput
    {
        public IReadOnlyList<AnswerCitation> Citations { get; init; } = [];
        public IReadOnlyList<string> Notes { get; init; } = [];
    }
}
