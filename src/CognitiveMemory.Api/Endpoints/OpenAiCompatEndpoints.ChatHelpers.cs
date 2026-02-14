using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CognitiveMemory.Api.Configuration;
using CognitiveMemory.Application.AI;
using CognitiveMemory.Application.AI.Plugins;
using CognitiveMemory.Application.Contracts;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;

namespace CognitiveMemory.Api.Endpoints;

public static partial class OpenAiCompatEndpoints
{
    private static string ResolveConversationKey(string? user)
    {
        if (!string.IsNullOrWhiteSpace(user))
        {
            return user.Trim();
        }

        return "anonymous";
    }

    private static OpenAiChatMetadata BuildChatMetadata(IReadOnlyList<OpenAiToolExecution> toolExecutions, string assistantText)
    {
        var uncertainty = new List<string>();
        var confidence = 0.65;
        var citations = ExtractCitations(toolExecutions);

        var successfulMemorySearches = toolExecutions
            .Where(x =>
                (x.ToolName == "memory_recall.search_claims" ||
                 x.ToolName == "memory_recall.search_claims_filtered") &&
                x.Ok)
            .ToList();

        var recalledFacts = successfulMemorySearches.Sum(x => x.ResultCount);
        if (successfulMemorySearches.Count == 0)
        {
            confidence = 0.55;
            uncertainty.Add("NoMemoryRecall");
        }
        else if (recalledFacts == 0)
        {
            confidence = 0.45;
            uncertainty.Add("NoRelevantMemory");
            uncertainty.Add("InsufficientEvidence");
        }
        else
        {
            confidence = Math.Clamp(0.62 + Math.Min(0.25, recalledFacts * 0.03), 0.0, 0.9);
        }

        if (LooksFactualAnswer(assistantText) && citations.Count == 0)
        {
            uncertainty.Add("InsufficientEvidence");
            uncertainty.Add("MissingCitations");
            confidence = Math.Min(confidence, 0.45);
        }

        if (citations.Count > 0)
        {
            confidence = Math.Min(0.92, confidence + 0.03);
        }

        var failedToolCount = toolExecutions.Count(x => !x.Ok);
        if (failedToolCount > 0)
        {
            uncertainty.Add("ToolFailures");
            confidence = Math.Max(0.35, confidence - (failedToolCount * 0.05));
        }

        return new OpenAiChatMetadata
        {
            Confidence = confidence,
            Citations = citations,
            UncertaintyFlags = uncertainty.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Contradictions = [],
            ToolExecutions = toolExecutions,
            Conscience = new AnswerConscience
            {
                Decision = "Approve",
                RiskScore = uncertainty.Count == 0 ? 0.2 : 0.35,
                PolicyVersion = "chat-agent-v2",
                ReasonCodes = uncertainty
            }
        };
    }

    private static IReadOnlyList<AnswerCitation> ExtractCitations(IReadOnlyList<OpenAiToolExecution> toolExecutions)
    {
        var citations = new List<AnswerCitation>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var execution in toolExecutions)
        {
            if (!execution.Ok ||
                !string.Equals(execution.ToolName, "memory_recall.get_evidence", StringComparison.OrdinalIgnoreCase) ||
                execution.Data is null)
            {
                continue;
            }

            var data = execution.Data.Value;
            if (data.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var row in data.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!TryReadGuid(row, "claimId", out var claimId) ||
                    !TryReadGuid(row, "evidenceId", out var evidenceId))
                {
                    continue;
                }

                var key = $"{claimId:D}:{evidenceId:D}";
                if (!seen.Add(key))
                {
                    continue;
                }

                citations.Add(new AnswerCitation
                {
                    ClaimId = claimId,
                    EvidenceId = evidenceId
                });
            }
        }

        return citations;
    }

    private static bool LooksFactualAnswer(string answer)
    {
        if (string.IsNullOrWhiteSpace(answer))
        {
            return false;
        }

        var normalized = answer.ToLowerInvariant();
        if (normalized.Contains("i don't know", StringComparison.Ordinal) ||
            normalized.Contains("not enough evidence", StringComparison.Ordinal) ||
            normalized.Contains("not sure", StringComparison.Ordinal))
        {
            return false;
        }

        if (Regex.IsMatch(normalized, @"\b\d{1,4}\b"))
        {
            return true;
        }

        var factualMarkers = new[]
        {
            "your name is",
            "you are",
            "your age",
            "according to",
            "based on",
            "evidence",
            "record",
            "memory",
            "claim"
        };

        return factualMarkers.Any(marker => normalized.Contains(marker, StringComparison.Ordinal));
    }

    private static string EnsureAssistantText(string? assistantReply, string requestId, ILogger logger)
    {
        if (!string.IsNullOrWhiteSpace(assistantReply))
        {
            return assistantReply;
        }

        logger.LogWarning("Empty answer content generated for request {RequestId}. Using fallback assistant message.", requestId);
        return "I do not have enough evidence to provide a reliable answer yet.";
    }

    private static bool HasSuccessfulToolCall(ToolExecutionCollector collector, string toolName)
    {
        return collector.Snapshot().Any(x =>
            string.Equals(x.ToolName, toolName, StringComparison.OrdinalIgnoreCase) && x.Ok);
    }

    private static bool HasSuccessfulAgentWrite(ToolExecutionCollector collector)
    {
        return collector.Snapshot().Any(x =>
            x.Ok &&
            string.Equals(x.Source, "agent", StringComparison.OrdinalIgnoreCase) &&
            x.ToolName.StartsWith("memory_write.", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task ApplyConfiguredChatPersistenceAsync(
        ChatPersistenceOptions options,
        ToolExecutionCollector collector,
        TrackingMemoryWritePlugin memoryWritePlugin,
        string requestId,
        string sourceRef,
        string model,
        string userMessage,
        string assistantMessage,
        string conversationKey,
        string userActorKey,
        string assistantActorKey,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        switch (options.Mode)
        {
            case ChatPersistenceMode.AgentOnly:
                return;
            case ChatPersistenceMode.HybridFallback:
                {
                    if (HasSuccessfulAgentWrite(collector))
                    {
                        return;
                    }

                    await TryPersistDurableUserFactsAsync(
                        memoryWritePlugin,
                        userMessage,
                        $"{sourceRef}:user",
                        userActorKey,
                        logger,
                        cancellationToken);

                    await PersistToolManagedChatMemoryAsync(
                        memoryWritePlugin,
                        requestId,
                        sourceRef,
                        model,
                        userMessage,
                        assistantMessage,
                        conversationKey,
                        userActorKey,
                        assistantActorKey,
                        options.IngestAssistantTurns,
                        logger,
                        cancellationToken);
                    return;
                }
            case ChatPersistenceMode.SystemPostTurn:
            default:
                {
                    if (!HasSuccessfulToolCall(collector, "memory_write.create_claim"))
                    {
                        await TryPersistDurableUserFactsAsync(
                            memoryWritePlugin,
                            userMessage,
                            $"{sourceRef}:user",
                            userActorKey,
                            logger,
                            cancellationToken);
                    }

                    await PersistToolManagedChatMemoryAsync(
                        memoryWritePlugin,
                        requestId,
                        sourceRef,
                        model,
                        userMessage,
                        assistantMessage,
                        conversationKey,
                        userActorKey,
                        assistantActorKey,
                        options.IngestAssistantTurns,
                        logger,
                        cancellationToken);
                    return;
                }
        }
    }

    private static async Task PersistToolManagedChatMemoryAsync(
        TrackingMemoryWritePlugin memoryWritePlugin,
        string requestId,
        string sourceRef,
        string model,
        string userMessage,
        string assistantMessage,
        string conversationKey,
        string userActorKey,
        string assistantActorKey,
        bool ingestAssistantTurn,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(userMessage))
        {
            await TryIngestNoteViaToolAsync(
                memoryWritePlugin,
                sourceRef: $"{sourceRef}:user",
                content: userMessage,
                metadataJson: JsonSerializer.Serialize(new
                {
                    model,
                    compat = "openai",
                    requestId,
                    actorRole = "user",
                    actorKey = userActorKey,
                    actorName = string.Empty,
                    conversationKey
                }),
                logger,
                cancellationToken);
        }

        if (!ingestAssistantTurn)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(assistantMessage))
        {
            return;
        }

        await TryIngestNoteViaToolAsync(
            memoryWritePlugin,
            sourceRef: $"{sourceRef}:assistant",
            content: assistantMessage,
            metadataJson: JsonSerializer.Serialize(new
            {
                model,
                compat = "openai",
                requestId,
                actorRole = "assistant",
                actorKey = assistantActorKey,
                actorName = model,
                conversationKey
            }),
            logger,
            cancellationToken);
    }

    private static async Task TryIngestNoteViaToolAsync(
        TrackingMemoryWritePlugin memoryWritePlugin,
        string sourceRef,
        string content,
        string metadataJson,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            await memoryWritePlugin.IngestNoteForSystemAsync(
                sourceRef: sourceRef,
                content: content,
                metadataJson: metadataJson,
                idempotencyKey: null,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Tool-based note ingest failed for sourceRef {SourceRef}.", sourceRef);
        }
    }

    private static async Task TryPersistDurableUserFactsAsync(
        TrackingMemoryWritePlugin memoryWritePlugin,
        string latestUserMessage,
        string sourceRef,
        string userActorKey,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var candidateName = TryExtractNameFact(latestUserMessage);
        if (candidateName is null)
        {
            return;
        }

        try
        {
            await memoryWritePlugin.CreateClaimForSystemAsync(
                subjectKey: userActorKey,
                predicate: "name",
                literalValue: candidateName,
                sourceRef: sourceRef,
                excerpt: latestUserMessage,
                confidence: 0.88,
                idempotencyKey: null,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Durable user fact persistence failed.");
        }
    }

    private static string? TryExtractNameFact(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var match = NameFactPattern.Match(message);
        if (!match.Success)
        {
            return null;
        }

        var raw = (match.Groups[1].Value ?? string.Empty).Trim(' ', '.', ',', ';', ':', '!', '?', '"');
        if (raw.Length is < 2 or > 48)
        {
            return null;
        }

        // Avoid obvious non-name captures.
        var lowered = raw.ToLowerInvariant();
        if (lowered.StartsWith("not ") || lowered.StartsWith("sure ") || lowered.Contains("what"))
        {
            return null;
        }

        var normalized = string.Join(' ', raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return normalized;
    }

    private static async Task<string> PrefetchMemoryContextAsync(
        TrackingMemoryRecallPlugin memoryRecallPlugin,
        string latestUserMessage,
        string userActorKey,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var predicateHint = InferPredicateHint(latestUserMessage);

            // Pull user-scoped memory on every turn so the chat response can stay grounded.
            var response = predicateHint is null
                ? await ExecutePrefetchStepAsync(
                    ct => memoryRecallPlugin.SearchClaimsForPrefetchAsync(
                        query: latestUserMessage,
                        topK: 4,
                        subjectFilter: userActorKey,
                        cancellationToken: ct),
                    PrefetchPrimaryTimeoutMs,
                    "search_claims.primary",
                    logger,
                    cancellationToken)
                : await ExecutePrefetchStepAsync(
                    ct => memoryRecallPlugin.SearchClaimsFilteredForPrefetchAsync(
                        query: latestUserMessage,
                        topK: 4,
                        subjectFilter: userActorKey,
                        predicateFilter: predicateHint,
                        minScore: 0.05,
                        cancellationToken: ct),
                    PrefetchPrimaryTimeoutMs,
                    "search_claims_filtered.primary",
                    logger,
                    cancellationToken);

            var claims = TryParseRecallClaims(response);
            if (claims.Count == 0 && predicateHint is not null)
            {
                var broadResponse = await ExecutePrefetchStepAsync(
                    ct => memoryRecallPlugin.SearchClaimsForPrefetchAsync(
                        query: latestUserMessage,
                        topK: 8,
                        subjectFilter: userActorKey,
                        cancellationToken: ct),
                    PrefetchFallbackTimeoutMs,
                    "search_claims.broad",
                    logger,
                    cancellationToken);
                claims = TryParseRecallClaims(broadResponse);
            }

            if (claims.Count == 0 && !string.IsNullOrWhiteSpace(predicateHint))
            {
                // Retry with the bare predicate keyword to recover from noisy lexical matches.
                var hintResponse = await ExecutePrefetchStepAsync(
                    ct => memoryRecallPlugin.SearchClaimsForPrefetchAsync(
                        query: predicateHint,
                        topK: 8,
                        subjectFilter: userActorKey,
                        cancellationToken: ct),
                    PrefetchFallbackTimeoutMs,
                    "search_claims.hint",
                    logger,
                    cancellationToken);
                claims = TryParseRecallClaims(hintResponse);
            }

            var relevantClaims = claims
                .Where(c => IsPrefetchRelevant(c, predicateHint))
                .ToList();

            if (relevantClaims.Count == 0 && claims.Count > 0)
            {
                relevantClaims = claims
                    .OrderByDescending(c => c.Score)
                    .ThenByDescending(c => c.Confidence)
                    .Take(Math.Min(4, claims.Count))
                    .ToList();
            }

            if (relevantClaims.Count == 0)
            {
                return "No user-scoped memory claims were recalled for this turn.";
            }

            var lines = relevantClaims
                .OrderByDescending(c => c.Score)
                .ThenByDescending(c => c.Confidence)
                .Take(8)
                .Select(c =>
                {
                    var literal = string.IsNullOrWhiteSpace(c.LiteralValue) ? "(empty)" : c.LiteralValue.Trim();
                    if (literal.Length > 140)
                    {
                        literal = $"{literal[..140]}...";
                    }

                    return $"- claimId={c.ClaimId}; predicate={c.Predicate}; value={literal}; confidence={c.Confidence:F2}; score={c.Score:F2}";
                });

            var detailClaims = relevantClaims
                .OrderByDescending(c => c.Score)
                .ThenByDescending(c => c.Confidence)
                .Take(2)
                .ToArray();

            var detailLines = new List<string>(capacity: detailClaims.Length);
            foreach (var detailClaim in detailClaims)
            {
                var detailLine = await ExecutePrefetchStepAsync(
                    ct => LoadClaimDetailsForPrefetchContextAsync(memoryRecallPlugin, detailClaim, ct),
                    PrefetchDetailTimeoutMs,
                    $"detail:{detailClaim.ClaimId}",
                    logger,
                    cancellationToken);
                if (!string.IsNullOrWhiteSpace(detailLine))
                {
                    detailLines.Add(detailLine);
                }
            }

            return string.Join('\n', lines.Concat(detailLines));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("Memory prefetch timed out for user actor key {UserActorKey}.", userActorKey);
            return "Memory prefetch timed out for this turn.";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Memory prefetch failed for user actor key {UserActorKey}.", userActorKey);
            return "Memory prefetch failed for this turn.";
        }
    }

    private static async Task<string> ExecutePrefetchStepAsync(
        Func<CancellationToken, Task<string>> operation,
        int timeoutMs,
        string stepName,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            return await operation(linkedCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug(
                "Memory prefetch step {StepName} timed out after {TimeoutMs}ms.",
                stepName,
                timeoutMs);
            return string.Empty;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Memory prefetch step {StepName} failed.", stepName);
            return string.Empty;
        }
    }

    private static bool IsPrefetchRelevant(RecalledClaim claim, string? predicateHint)
    {
        if (string.Equals(predicateHint, "name", StringComparison.OrdinalIgnoreCase))
        {
            return claim.Score >= 0.15 ||
                   claim.Predicate.Contains("name", StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrWhiteSpace(predicateHint))
        {
            return claim.Score >= 0.25 ||
                   claim.Predicate.Contains(predicateHint, StringComparison.OrdinalIgnoreCase);
        }

        return claim.Score >= 0.35 || claim.Confidence >= 0.75;
    }

    private static async Task<string> GenerateChatReplyWithToolsAsync(
        string model,
        IReadOnlyList<OpenAiIncomingMessage> messages,
        IMemoryKernelFactory kernelFactory,
        object memoryRecallPlugin,
        object memoryWritePlugin,
        object memoryGovernancePlugin,
        object groundingPlugin,
        string preloadedMemoryContext,
        string userActorKey,
        ILogger logger,
        Func<string, CancellationToken, Task>? onDelta,
        CancellationToken cancellationToken)
    {
        try
        {
            var kernel = kernelFactory.CreateKernel();
            kernel.Plugins.AddFromObject(memoryRecallPlugin, "memory_recall");
            kernel.Plugins.AddFromObject(memoryWritePlugin, "memory_write");
            kernel.Plugins.AddFromObject(memoryGovernancePlugin, "memory_governance");
            kernel.Plugins.AddFromObject(groundingPlugin, "grounding");

            var agent = new ChatCompletionAgent
            {
                Name = "CognitiveMemoryChat",
                Kernel = kernel,
                Instructions = BuildChatAgentInstructions(model, userActorKey)
            };

            var input = BuildChatAgentInput(messages, preloadedMemoryContext, userActorKey);
            var prompt = new ChatMessageContent(AuthorRole.User, input);
            var complete = new StringBuilder();
            var latestObserved = string.Empty;

            await foreach (var response in agent.InvokeStreamingAsync(prompt, cancellationToken: cancellationToken).WithCancellation(cancellationToken))
            {
                var text = ExtractAgentText(response);
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                var delta = text;
                if (!string.IsNullOrEmpty(latestObserved))
                {
                    if (string.Equals(text, latestObserved, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (text.StartsWith(latestObserved, StringComparison.Ordinal))
                    {
                        delta = text[latestObserved.Length..];
                        latestObserved = text;
                    }
                    else if (latestObserved.EndsWith(text, StringComparison.Ordinal))
                    {
                        continue;
                    }
                    else
                    {
                        latestObserved += text;
                    }
                }
                else
                {
                    latestObserved = text;
                }

                if (delta.Length == 0)
                {
                    continue;
                }

                complete.Append(delta);
                if (onDelta is not null)
                {
                    await onDelta(delta, cancellationToken);
                }
            }

            var final = complete.ToString().Trim();
            return string.IsNullOrWhiteSpace(final)
                ? "I can help with that. Could you clarify what you want me to do next?"
                : final;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Tool-augmented chat agent failed; returning fallback response.");
            return "I hit a temporary issue while reasoning with tools. Please try again.";
        }
    }

    private static string BuildChatAgentInstructions(string model, string userActorKey)
    {
        var template = PromptLoader.LoadText(PromptCatalog.ChatAgentSystemPromptPath);
        return PromptLoader.RenderTemplate(template, new Dictionary<string, string?>
        {
            ["model"] = model,
            ["userActorKey"] = userActorKey
        });
    }

    private static string BuildChatAgentInput(IReadOnlyList<OpenAiIncomingMessage> messages, string preloadedMemoryContext, string userActorKey)
    {
        if (messages.Count == 0)
        {
            return string.Empty;
        }

        var maxMessages = 16;
        var start = Math.Max(0, messages.Count - maxMessages);
        var builder = new StringBuilder();

        builder.AppendLine("Conversation transcript:");
        for (var index = start; index < messages.Count; index++)
        {
            var message = messages[index];
            var role = NormalizeRole(message.Role);
            if (role is null)
            {
                continue;
            }

            var content = (message.Content ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            builder.Append(role.ToUpperInvariant())
                .Append(": ")
                .AppendLine(content);
        }

        builder.AppendLine();
        builder.AppendLine($"User actor key: {userActorKey}");
        var latestUserContent = messages
            .LastOrDefault(m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase))
            ?.Content ?? string.Empty;
        var introducedName = TryExtractNameFact(latestUserContent);
        if (!string.IsNullOrWhiteSpace(introducedName))
        {
            builder.AppendLine($"Turn requirement: user directly introduced name \"{introducedName}\". Call memory_write.create_claim before final reply.");
        }
        builder.AppendLine("Preloaded memory context (derived via memory_recall.search_claims):");
        builder.AppendLine(string.IsNullOrWhiteSpace(preloadedMemoryContext) ? "(none)" : preloadedMemoryContext);
        builder.AppendLine();
        builder.AppendLine("Tooling policy:");
        builder.AppendLine("1) For identity/profile questions (name/age/email/location/preferences), run memory_recall before answering.");
        builder.AppendLine("2) If search_claims_filtered returns zero rows, retry immediately with broad search_claims using the same subjectFilter.");
        builder.AppendLine("3) Use get_claim/get_evidence on top hits before answering factual memory questions.");
        builder.AppendLine("4) Do not ask the user to repeat a fact unless this turn's filtered and broad recalls both returned zero rows.");
        builder.AppendLine("Response policy: never print tool-call traces; respond with plain assistant prose only.");
        builder.AppendLine();
        builder.AppendLine("Reply as ASSISTANT to the latest USER message.");
        return builder.ToString().Trim();
    }

    private static string? NormalizeRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return null;
        }

        if (string.Equals(role, "system", StringComparison.OrdinalIgnoreCase))
        {
            return "system";
        }

        if (string.Equals(role, "user", StringComparison.OrdinalIgnoreCase))
        {
            return "user";
        }

        if (string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase))
        {
            return "assistant";
        }

        return null;
    }

    private static int FindLatestUserMessageIndex(IReadOnlyList<OpenAiIncomingMessage> messages)
    {
        for (var index = messages.Count - 1; index >= 0; index--)
        {
            if (string.Equals(messages[index].Role, "user", StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool ShouldPrefetchMemory(IReadOnlyList<OpenAiIncomingMessage> messages, string latestUserContent)
    {
        if (string.IsNullOrWhiteSpace(latestUserContent))
        {
            return false;
        }

        _ = messages;
        return true;
    }

    private static string? InferPredicateHint(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var normalized = query.ToLowerInvariant();
        if (normalized.Contains("name", StringComparison.Ordinal))
        {
            return "name";
        }

        if (normalized.Contains("age", StringComparison.Ordinal) ||
            normalized.Contains("old", StringComparison.Ordinal))
        {
            return "age";
        }

        if (normalized.Contains("email", StringComparison.Ordinal))
        {
            return "email";
        }

        if (normalized.Contains("location", StringComparison.Ordinal) ||
            normalized.Contains("live", StringComparison.Ordinal))
        {
            return "location";
        }

        return null;
    }

    private static string? ExtractTextLikeContent(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is string text)
        {
            return text;
        }

        var type = value.GetType();

        var contentProperty = type.GetProperty("Content");
        if (contentProperty?.GetValue(value) is string content && content.Length > 0)
        {
            return content;
        }

        var textProperty = type.GetProperty("Text");
        if (textProperty?.GetValue(value) is string textValue && textValue.Length > 0)
        {
            return textValue;
        }

        var itemsProperty = type.GetProperty("Items");
        if (itemsProperty?.GetValue(value) is IEnumerable items)
        {
            var builder = new StringBuilder();
            foreach (var item in items)
            {
                var itemText = ExtractTextLikeContent(item);
                if (!string.IsNullOrEmpty(itemText))
                {
                    builder.Append(itemText);
                }
            }

            if (builder.Length > 0)
            {
                return builder.ToString();
            }
        }

        var fallback = value.ToString();
        var typeName = type.FullName ?? type.Name;
        if (string.IsNullOrWhiteSpace(fallback) || string.Equals(fallback, typeName, StringComparison.Ordinal))
        {
            return null;
        }

        return fallback;
    }

    private static string? ExtractAgentText(object? response)
    {
        if (response is null)
        {
            return null;
        }

        if (response is ChatMessageContent messageContent)
        {
            return ExtractTextLikeContent(messageContent);
        }

        if (response is StreamingChatMessageContent streamingMessageContent)
        {
            return ExtractTextLikeContent(streamingMessageContent);
        }

        var responseType = response.GetType();
        var messageProperty = responseType.GetProperty("Message");
        var messageValue = messageProperty?.GetValue(response);
        if (messageValue is ChatMessageContent chatMessageContent)
        {
            return ExtractTextLikeContent(chatMessageContent);
        }

        if (messageValue is StreamingChatMessageContent streamingChatMessageContent)
        {
            return ExtractTextLikeContent(streamingChatMessageContent);
        }

        return ExtractTextLikeContent(response);
    }

    private static IReadOnlyList<RecalledClaim> TryParseRecallClaims(string envelopeJson)
    {
        if (string.IsNullOrWhiteSpace(envelopeJson))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(envelopeJson);
            var root = document.RootElement;
            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var claims = new List<RecalledClaim>();
            foreach (var item in data.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!TryReadGuid(item, "claimId", out var claimId))
                {
                    continue;
                }

                var predicate = ReadString(item, "predicate");
                if (string.IsNullOrWhiteSpace(predicate))
                {
                    continue;
                }

                var literalValue = ReadString(item, "literalValue");
                var confidence = ReadDouble(item, "confidence", 0.0);
                var score = ReadDouble(item, "score", 0.0);
                claims.Add(new RecalledClaim(claimId, predicate, literalValue, confidence, score));
            }

            return claims;
        }
        catch
        {
            return [];
        }
    }

    private static bool TryReadGuid(JsonElement element, string propertyName, out Guid value)
    {
        value = Guid.Empty;
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.String && Guid.TryParse(property.GetString(), out var parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return string.Empty;
        }

        return property.GetString() ?? string.Empty;
    }

    private static double ReadDouble(JsonElement element, string propertyName, double fallback)
    {
        if (!element.TryGetProperty(propertyName, out var property) || !property.TryGetDouble(out var value))
        {
            return fallback;
        }

        return value;
    }

    private static async Task<string> LoadClaimDetailsForPrefetchContextAsync(
        TrackingMemoryRecallPlugin memoryRecallPlugin,
        RecalledClaim claim,
        CancellationToken cancellationToken)
    {
        try
        {
            // Keep prefetch detail calls sequential within a request scope to avoid concurrent DbContext usage.
            var claimEnvelope = await memoryRecallPlugin.GetClaimForPrefetchAsync(claim.ClaimId, cancellationToken);
            var evidenceEnvelope = await memoryRecallPlugin.GetEvidenceForPrefetchAsync(claim.ClaimId, cancellationToken);

            var claimLoaded = TryReadEnvelopeOk(claimEnvelope);
            var evidenceCount = TryCountEnvelopeDataRecords(evidenceEnvelope);
            return $"- detail claimId={claim.ClaimId}; claimLoaded={claimLoaded}; evidenceCount={evidenceCount}";
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool TryReadEnvelopeOk(string envelopeJson)
    {
        if (string.IsNullOrWhiteSpace(envelopeJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(envelopeJson);
            var root = document.RootElement;
            return root.TryGetProperty("ok", out var ok) &&
                   ok.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                   ok.GetBoolean();
        }
        catch
        {
            return false;
        }
    }

    private static int TryCountEnvelopeDataRecords(string envelopeJson)
    {
        if (string.IsNullOrWhiteSpace(envelopeJson))
        {
            return 0;
        }

        try
        {
            using var document = JsonDocument.Parse(envelopeJson);
            var root = document.RootElement;
            if (!root.TryGetProperty("data", out var data))
            {
                return 0;
            }

            if (data.ValueKind == JsonValueKind.Array)
            {
                return data.GetArrayLength();
            }

            if (data.ValueKind == JsonValueKind.Object ||
                data.ValueKind == JsonValueKind.String ||
                data.ValueKind == JsonValueKind.Number ||
                data.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return 1;
            }

            return 0;
        }
        catch
        {
            return 0;
        }
    }

    private static async Task WriteSseAsync(StreamWriter writer, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, SseJsonOptions);
        await writer.WriteAsync($"data: {json}\n\n");
        await writer.FlushAsync(cancellationToken);
    }
}
