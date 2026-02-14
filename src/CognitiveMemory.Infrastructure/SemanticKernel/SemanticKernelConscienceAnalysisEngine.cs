using System.Text.Json;
using CognitiveMemory.Application.AI;
using CognitiveMemory.Application.AI.Tooling;
using CognitiveMemory.Application.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;

namespace CognitiveMemory.Infrastructure.SemanticKernel;

public sealed class SemanticKernelConscienceAnalysisEngine(
    IMemoryKernelFactory kernelFactory,
    IOptions<SemanticKernelOptions> options,
    ILogger<SemanticKernelConscienceAnalysisEngine> logger) : IConscienceAnalysisEngine
{
    public async Task<ConscienceAnalysisResult> AnalyzeClaimAsync(ConscienceAnalysisInput input, CancellationToken cancellationToken)
    {
        var fallback = BuildFallback(input.Claim);

        try
        {
            var kernel = kernelFactory.CreateKernel();
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(3, options.Value.ConscienceAnalysisTimeoutSeconds)));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var prompt = await PromptLoader.LoadTextAsync(PromptCatalog.ConscienceAnalyzerPromptPath, cancellationToken);
            var function = kernel.CreateFunctionFromPrompt(prompt, functionName: PromptCatalog.ConscienceAnalyzer);

            var promptInput = JsonSerializer.Serialize(new
            {
                sourceEventId = input.SourceEventId,
                sourceEventType = input.SourceEventType,
                claim = new
                {
                    input.Claim.ClaimId,
                    input.Claim.Predicate,
                    input.Claim.LiteralValue,
                    input.Claim.Confidence,
                    evidence = input.Claim.Evidence,
                    contradictions = input.Claim.Contradictions,
                    input.Claim.Scope,
                    input.Claim.ValidFrom,
                    input.Claim.ValidTo,
                    input.Claim.LastReinforcedAt
                }
            });

            var result = await kernel.InvokeAsync(function, new KernelArguments { ["input"] = promptInput }, linkedCts.Token);
            var raw = result.GetValue<string>() ?? string.Empty;
            var parsed = TryParse(raw);
            if (parsed is not null)
            {
                return new ConscienceAnalysisResult
                {
                    Decision = parsed.Decision,
                    RiskScore = parsed.RiskScore,
                    RecommendedConfidence = parsed.RecommendedConfidence,
                    ReasonCodes = parsed.ReasonCodes,
                    Summary = parsed.Summary,
                    Keywords = parsed.Keywords,
                    UsedFallback = false,
                    ModelId = options.Value.ChatModelId
                };
            }

            logger.LogWarning("Conscience analysis returned unparsable payload; using fallback.");
            return CloneWithFallback(fallback, options.Value.ChatModelId);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Conscience analysis timed out after {TimeoutSeconds}s; using fallback.", options.Value.ConscienceAnalysisTimeoutSeconds);
            return CloneWithFallback(fallback, options.Value.ChatModelId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Conscience analysis failed; using fallback.");
            return CloneWithFallback(fallback, options.Value.ChatModelId);
        }
    }

    private static ConscienceAnalysisResult BuildFallback(QueryCandidate claim)
    {
        var openContradictions = claim.Contradictions.Count(c => string.Equals(c.Status, "Open", StringComparison.OrdinalIgnoreCase));
        var weakEvidence = claim.Evidence.Count == 0 || claim.Evidence.Average(x => x.Strength) < 0.5;

        var decision = openContradictions switch
        {
            >= 2 => ConsciencePolicy.Revise,
            1 => ConsciencePolicy.Downgrade,
            _ => weakEvidence ? ConsciencePolicy.Downgrade : ConsciencePolicy.Approve
        };

        var reasonCodes = new List<string> { ConscienceReasonCodes.HeuristicFallback, ConscienceReasonCodes.ContradictionAnalyst, ConscienceReasonCodes.Calibrator };
        if (weakEvidence)
        {
            reasonCodes.Add(ConscienceReasonCodes.WeakEvidenceSupport);
        }

        if (openContradictions > 0)
        {
            reasonCodes.Add(ConscienceReasonCodes.OpenContradictionsPresent);
        }

        var calibrationPenalty = Math.Min(0.35, openContradictions * 0.1) + (weakEvidence ? 0.1 : 0.0);
        var recommendedConfidence = Math.Clamp(claim.Confidence - calibrationPenalty, 0.05, 1.0);

        return new ConscienceAnalysisResult
        {
            Decision = decision,
            RiskScore = decision switch
            {
                ConsciencePolicy.Revise => 0.8,
                ConsciencePolicy.Downgrade => 0.45,
                _ => 0.2
            },
            RecommendedConfidence = recommendedConfidence,
            ReasonCodes = reasonCodes.Distinct().ToList(),
            Summary = BuildSummary(claim),
            Keywords = BuildKeywords(claim),
            UsedFallback = true,
            ModelId = "heuristic"
        };
    }

    private static string BuildSummary(QueryCandidate claim)
    {
        var literal = string.IsNullOrWhiteSpace(claim.LiteralValue) ? "(non-literal claim)" : claim.LiteralValue;
        return $"Claim '{claim.Predicate}' currently indicates '{literal}' with confidence {Math.Round(claim.Confidence, 2)}.";
    }

    private static IReadOnlyList<string> BuildKeywords(QueryCandidate claim)
    {
        var tokens = new List<string>();
        if (!string.IsNullOrWhiteSpace(claim.Predicate))
        {
            tokens.Add(claim.Predicate);
        }

        if (!string.IsNullOrWhiteSpace(claim.LiteralValue))
        {
            tokens.AddRange(claim.LiteralValue
                .Split([' ', ',', '.', ';', ':', '/', '-', '_'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Take(4));
        }

        return tokens
            .Select(t => t.Trim())
            .Where(t => t.Length > 1)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
    }

    private static ParsedConscienceResult? TryParse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            if (!root.TryGetProperty("decision", out var decisionEl) || decisionEl.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var decision = decisionEl.GetString();
            if (string.IsNullOrWhiteSpace(decision))
            {
                return null;
            }

            var normalizedDecision = ConsciencePolicy.DecisionSet
                .FirstOrDefault(x => string.Equals(x, decision, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(normalizedDecision))
            {
                return null;
            }

            var riskScore = 0.2;
            if (root.TryGetProperty("riskScore", out var riskEl) && riskEl.TryGetDouble(out var parsedRisk))
            {
                riskScore = Math.Clamp(parsedRisk, 0, 1);
            }

            var recommendedConfidence = 0.5;
            if (root.TryGetProperty("recommendedConfidence", out var confEl) && confEl.TryGetDouble(out var parsedConf))
            {
                recommendedConfidence = Math.Clamp(parsedConf, 0, 1);
            }

            var reasonCodes = new List<string>();
            if (root.TryGetProperty("reasonCodes", out var reasonsEl) && reasonsEl.ValueKind == JsonValueKind.Array)
            {
                reasonCodes.AddRange(reasonsEl.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString())
                    .Where(x => !string.IsNullOrWhiteSpace(x))!
                    .Cast<string>());
            }

            var summary = string.Empty;
            if (root.TryGetProperty("summary", out var summaryEl) && summaryEl.ValueKind == JsonValueKind.String)
            {
                summary = summaryEl.GetString() ?? string.Empty;
            }

            var keywords = new List<string>();
            if (root.TryGetProperty("keywords", out var keywordsEl) && keywordsEl.ValueKind == JsonValueKind.Array)
            {
                keywords.AddRange(keywordsEl.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString())
                    .Where(x => !string.IsNullOrWhiteSpace(x))!
                    .Cast<string>());
            }

            if (reasonCodes.Count == 0)
            {
                reasonCodes.Add(ConscienceReasonCodes.LlmConscience);
            }

            return new ParsedConscienceResult
            {
                Decision = normalizedDecision,
                RiskScore = riskScore,
                RecommendedConfidence = recommendedConfidence,
                ReasonCodes = reasonCodes,
                Summary = summary,
                Keywords = keywords
            };
        }
        catch
        {
            return null;
        }
    }

    private static ConscienceAnalysisResult CloneWithFallback(ConscienceAnalysisResult fallback, string modelId)
    {
        return new ConscienceAnalysisResult
        {
            Decision = fallback.Decision,
            RiskScore = fallback.RiskScore,
            RecommendedConfidence = fallback.RecommendedConfidence,
            ReasonCodes = fallback.ReasonCodes,
            Summary = fallback.Summary,
            Keywords = fallback.Keywords,
            UsedFallback = true,
            ModelId = modelId
        };
    }

    private sealed class ParsedConscienceResult : ConscienceAnalysisResult
    {
    }
}
