using System.ComponentModel;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;
using CognitiveMemory.Application.Abstractions;
using CognitiveMemory.Application.Cognitive;
using CognitiveMemory.Domain.Memory;
using CognitiveMemory.Infrastructure.Companions;
using CognitiveMemory.Infrastructure.Scheduling;
using CognitiveMemory.Infrastructure.SemanticKernel;
using CognitiveMemory.Application.Relationships;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.Ollama;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace CognitiveMemory.Infrastructure.SemanticKernel.Plugins;

public sealed class MemoryToolsPlugin(
    IWorkingMemoryStore workingMemoryStore,
    IEpisodicMemoryRepository episodicMemoryRepository,
    ISemanticMemoryRepository semanticMemoryRepository,
    IProceduralMemoryRepository proceduralMemoryRepository,
    ISelfModelRepository selfModelRepository,
    IMemoryRelationshipRepository memoryRelationshipRepository,
    IMemoryRelationshipExtractionService memoryRelationshipExtractionService,
    IToolInvocationAuditRepository toolInvocationAuditRepository,
    IScheduledActionStore scheduledActionStore,
    ICompanionScopeResolver companionScopeResolver,
    ICompanionCognitiveProfileResolver cognitiveProfileResolver,
    ClaimExtractionKernel routingKernel,
    SemanticKernelOptions semanticKernelOptions,
    ILogger<MemoryToolsPlugin> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string ToolStoreMemory = "store_memory";
    private const string ToolRetrieveMemory = "retrieve_memory";
    private const string ToolGetCurrentTime = "get_current_time";
    private const string ToolScheduleAction = "schedule_action";
    private const string ToolListScheduledActions = "list_scheduled_actions";
    private const string ToolCancelScheduledAction = "cancel_scheduled_action";
    private const string ToolCreateMemoryRelationship = "create_memory_relationship";
    private const string ToolGetMemoryRelationships = "get_memory_relationships";
    private const string ToolBackfillMemoryRelationships = "backfill_memory_relationships";
    private const string ToolExtractMemoryRelationships = "extract_memory_relationships";
    private const string RoutingPluginName = "MemoryToolsRoutingReadOnly";
    private const int MaxRequestCacheEntries = 128;

    private readonly ConcurrentDictionary<string, Lazy<Task<LayerCandidateBatch>>> layerCandidateCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyList<SemanticClaim>>>> semanticTermCache = new(StringComparer.Ordinal);

    [KernelFunction(ToolStoreMemory)]
    [Description("Store memory for this session. A smaller routing model automatically chooses the best layer.")]
    public async Task<string> StoreMemoryAsync(
        [Description("Session id")] string sessionId,
        [Description("Memory content to persist")] string memoryText,
        [Description("Optional hint about intent or importance")] string? hint = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedSessionId = RequireNonEmpty(sessionId, nameof(sessionId));
        var normalizedText = RequireNonEmpty(memoryText, nameof(memoryText));
        var normalizedHint = string.IsNullOrWhiteSpace(hint) ? null : hint.Trim();

        return await ExecuteAsync(
            ToolStoreMemory,
            isWrite: true,
            new { sessionId = normalizedSessionId, memoryText = normalizedText, hint = normalizedHint },
            async () =>
            {
                var cognitiveProfile = await ResolveCognitiveProfileAsync(normalizedSessionId, cancellationToken);
                var writeConfidenceThreshold = Math.Clamp(cognitiveProfile.Profile.Memory.WriteThresholds.ConfidenceMin, 0, 1);
                var routes = (await ResolveStoreRoutesAsync(normalizedSessionId, normalizedText, normalizedHint, cancellationToken))
                    .Where(
                        route =>
                        {
                            var routeLayer = NormalizeLayer(route.Layer) ?? "working";
                            if (routeLayer == "working")
                            {
                                return true;
                            }

                            var confidence = Math.Clamp(route.Confidence ?? 0.7, 0, 1);
                            return confidence >= writeConfidenceThreshold;
                        })
                    .ToArray();
                if (routes.Length == 0)
                {
                    return JsonSerializer.Serialize(
                        new
                        {
                            layer = "none",
                            stored = false,
                            reason = "below_write_confidence_threshold",
                            threshold = writeConfidenceThreshold
                        },
                        JsonOptions);
                }

                logger.LogInformation(
                    "store_memory resolved routes. SessionId={SessionId} Count={Count} Layers={Layers}",
                    normalizedSessionId,
                    routes.Length,
                    string.Join(",", routes.Select(x => NormalizeLayer(x.Layer) ?? "unknown")));
                if (routes.Length == 1)
                {
                    return await PersistStoreRouteAsync(normalizedSessionId, normalizedText, routes[0], cancellationToken);
                }

                var results = new List<object?>(routes.Length);
                foreach (var route in routes)
                {
                    var resultJson = await PersistStoreRouteAsync(normalizedSessionId, normalizedText, route, cancellationToken);
                    results.Add(ParseJsonOrString(resultJson));
                }

                return JsonSerializer.Serialize(
                    new
                    {
                        layer = "multi",
                        count = results.Count,
                        results
                    },
                    JsonOptions);
            },
            cancellationToken);
    }

    [KernelFunction(ToolRetrieveMemory)]
    [Description("Retrieve memory for this session. A smaller routing model selects relevant layers unless layer is explicitly set.")]
    public async Task<string> RetrieveMemoryAsync(
        [Description("Session id")] string sessionId,
        [Description("What to retrieve")] string query,
        [Description("Maximum items to return")] int take = 20,
        [Description("Optional layer override: working|episodic|semantic|procedural|self|all")] string? layer = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedSessionId = RequireNonEmpty(sessionId, nameof(sessionId));
        var normalizedQuery = query?.Trim() ?? string.Empty;
        var normalizedTake = Math.Clamp(take, 1, 100);
        var normalizedLayer = string.IsNullOrWhiteSpace(layer) ? null : layer.Trim();

        return await ExecuteAsync(
            ToolRetrieveMemory,
            isWrite: false,
            new { sessionId = normalizedSessionId, query = normalizedQuery, take = normalizedTake, layer = normalizedLayer },
            async () =>
            {
                var cognitiveProfile = await ResolveCognitiveProfileAsync(normalizedSessionId, cancellationToken);
                var effectiveTake = Math.Clamp(normalizedTake, 1, Math.Clamp(cognitiveProfile.RuntimePolicy.Limits.MaxRetrieveResults, 1, 100));
                var queryProfile = BuildQueryProfile(normalizedQuery, effectiveTake);
                var selectedLayers = (await ResolveRetrieveLayersAsync(queryProfile.RawQuery, normalizedLayer, cognitiveProfile.Profile, cancellationToken))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var startedAt = DateTimeOffset.UtcNow;

                // Repositories share a scoped DbContext; execute layer retrieval serially to avoid overlapping EF operations.
                var layerBatches = new List<LayerCandidateBatch>(selectedLayers.Length);
                foreach (var layerName in selectedLayers)
                {
                    var batch = await RetrieveLayerCandidatesAsync(layerName, normalizedSessionId, queryProfile, cancellationToken);
                    layerBatches.Add(batch);
                }
                var candidates = layerBatches
                    .SelectMany(x => x.Candidates)
                    .ToArray();
                var semanticClaimIds = candidates
                    .Select(x => x.Payload)
                    .OfType<SemanticClaim>()
                    .Select(x => x.ClaimId)
                    .Distinct()
                    .ToArray();
                var semanticRelationshipDegree = await memoryRelationshipRepository.GetSemanticRelationshipDegreeAsync(
                    normalizedSessionId,
                    semanticClaimIds,
                    cancellationToken);
                var ranked = DeduplicateRankedCandidates(RankCandidates(candidates, queryProfile, semanticRelationshipDegree, cognitiveProfile.Profile))
                    .Take(Math.Min(
                        Math.Clamp(cognitiveProfile.RuntimePolicy.Limits.MaxRetrieveCandidates, 10, 400),
                        Math.Max(queryProfile.Take * selectedLayers.Length, queryProfile.Take)))
                    .ToArray();
                var results = BuildLayerResults(selectedLayers, ranked, queryProfile.Take);
                var insights = BuildInsights(queryProfile, ranked);
                var evidence = ranked
                    .Take(Math.Min(12, queryProfile.Take * 2))
                    .Select(x => new
                    {
                        layer = x.Candidate.Layer,
                        score = x.Score,
                        preview = TruncateForLog(x.Candidate.Text, 220)
                    })
                    .ToArray();
                var elapsedMs = Math.Max(0, (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);
                logger.LogInformation(
                    "Memory retrieval completed. Query={Query} Layers={LayerCount} Candidates={CandidateCount} Ranked={RankedCount} ElapsedMs={ElapsedMs}",
                    TruncateForLog(normalizedQuery, 120),
                    selectedLayers.Length,
                    candidates.Length,
                    ranked.Length,
                    elapsedMs);

                var payload = new
                {
                    sessionId = normalizedSessionId,
                    query = normalizedQuery,
                    selectedLayers,
                    results,
                    insights,
                    evidence,
                    layers = layerBatches.Select(
                        x => new
                        {
                            layer = x.Layer,
                            candidateCount = x.Candidates.Count,
                            elapsedMs = x.ElapsedMs,
                            fromCache = x.FromCache
                        }),
                    metrics = new
                    {
                        candidateCount = candidates.Length,
                        rankedCount = ranked.Length,
                        elapsedMs,
                        profileVersionId = cognitiveProfile.ProfileVersionId
                    }
                };

                return JsonSerializer.Serialize(payload, JsonOptions);
            },
            cancellationToken);
    }

    [KernelFunction(ToolGetCurrentTime)]
    [Description("Get current time details. Optionally provide a UTC offset like +02:00 or -05:00.")]
    public async Task<string> GetCurrentTimeAsync(
        [Description("Optional UTC offset in format +/-HH:mm")] string? utcOffset = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedOffset = string.IsNullOrWhiteSpace(utcOffset) ? null : utcOffset.Trim();
        return await ExecuteAsync(
            ToolGetCurrentTime,
            isWrite: false,
            new { utcOffset = normalizedOffset },
            () =>
            {
                var nowUtc = DateTimeOffset.UtcNow;
                var payload = new Dictionary<string, object?>
                {
                    ["utc"] = nowUtc.ToString("O", CultureInfo.InvariantCulture),
                    ["local"] = nowUtc.ToLocalTime().ToString("O", CultureInfo.InvariantCulture),
                    ["unixTimeSeconds"] = nowUtc.ToUnixTimeSeconds(),
                    ["unixTimeMilliseconds"] = nowUtc.ToUnixTimeMilliseconds(),
                    ["localTimeZone"] = TimeZoneInfo.Local.Id
                };

                if (TryParseUtcOffset(normalizedOffset, out var offset))
                {
                    payload["requestedUtcOffset"] = normalizedOffset;
                    payload["atRequestedOffset"] = nowUtc.ToOffset(offset).ToString("O", CultureInfo.InvariantCulture);
                }
                else if (!string.IsNullOrWhiteSpace(normalizedOffset))
                {
                    payload["requestedUtcOffset"] = normalizedOffset;
                    payload["warning"] = "Invalid utcOffset format. Expected +/-HH:mm.";
                }

                return Task.FromResult(JsonSerializer.Serialize(payload, JsonOptions));
            },
            cancellationToken);
    }

    [KernelFunction(ToolScheduleAction)]
    [Description("Schedule an action for a specific UTC datetime with JSON inputs. Supported actionType values: append_episodic, queue_subconscious_debate, store_memory, execute_procedural_trigger, invoke_webhook.")]
    public async Task<string> ScheduleActionAsync(
        [Description("Session id")] string sessionId,
        [Description("Action type to execute later")] string actionType,
        [Description("Run time in UTC ISO-8601 format, e.g. 2026-02-22T12:30:00Z")] string runAtUtc,
        [Description("JSON object string with action inputs")] string? inputJson = null,
        [Description("Optional max retries before failing")] int maxAttempts = 3,
        CancellationToken cancellationToken = default)
    {
        var normalizedSessionId = RequireNonEmpty(sessionId, nameof(sessionId));
        var normalizedActionType = RequireNonEmpty(actionType, nameof(actionType));
        if (!DateTimeOffset.TryParse(runAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsedRunAt))
        {
            throw new ArgumentException("runAtUtc must be a valid UTC datetime.");
        }

        var normalizedInput = string.IsNullOrWhiteSpace(inputJson) ? "{}" : inputJson.Trim();
        _ = JsonDocument.Parse(normalizedInput);
        return await ExecuteAsync(
            ToolScheduleAction,
            isWrite: true,
            new { sessionId = normalizedSessionId, actionType = normalizedActionType, runAtUtc = parsedRunAt, inputJson = normalizedInput, maxAttempts },
            async () =>
            {
                var created = await scheduledActionStore.ScheduleAsync(
                    normalizedSessionId,
                    normalizedActionType,
                    normalizedInput,
                    parsedRunAt,
                    Math.Clamp(maxAttempts, 1, 20),
                    cancellationToken);
                return JsonSerializer.Serialize(
                    new
                    {
                        scheduled = true,
                        actionId = created.ActionId,
                        created.SessionId,
                        created.ActionType,
                        created.RunAtUtc,
                        created.Status,
                        created.MaxAttempts
                    },
                    JsonOptions);
            },
            cancellationToken);
    }

    [KernelFunction(ToolListScheduledActions)]
    [Description("List scheduled actions for a session.")]
    public async Task<string> ListScheduledActionsAsync(
        [Description("Session id")] string sessionId,
        [Description("Optional status filter: Pending|Running|Completed|Failed|Canceled")] string? status = null,
        [Description("Maximum number of rows")] int take = 50,
        CancellationToken cancellationToken = default)
    {
        var normalizedSessionId = RequireNonEmpty(sessionId, nameof(sessionId));
        var normalizedStatus = string.IsNullOrWhiteSpace(status) ? null : status.Trim();
        return await ExecuteAsync(
            ToolListScheduledActions,
            isWrite: false,
            new { sessionId = normalizedSessionId, status = normalizedStatus, take },
            async () =>
            {
                var rows = await scheduledActionStore.ListAsync(
                    normalizedSessionId,
                    normalizedStatus,
                    Math.Clamp(take, 1, 200),
                    cancellationToken);
                return JsonSerializer.Serialize(
                    new
                    {
                        sessionId = normalizedSessionId,
                        count = rows.Count,
                        actions = rows.Select(
                            x => new
                            {
                                x.ActionId,
                                x.ActionType,
                                x.RunAtUtc,
                                x.Status,
                                x.Attempts,
                                x.MaxAttempts,
                                x.LastError,
                                x.CreatedAtUtc,
                                x.UpdatedAtUtc,
                                x.CompletedAtUtc
                            })
                    },
                    JsonOptions);
            },
            cancellationToken);
    }

    [KernelFunction(ToolCancelScheduledAction)]
    [Description("Cancel a scheduled action by actionId.")]
    public async Task<string> CancelScheduledActionAsync(
        [Description("Scheduled action id")] string actionId,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(actionId?.Trim(), out var parsedActionId))
        {
            throw new ArgumentException("actionId must be a valid GUID.");
        }

        return await ExecuteAsync(
            ToolCancelScheduledAction,
            isWrite: true,
            new { actionId = parsedActionId },
            async () =>
            {
                var canceled = await scheduledActionStore.CancelAsync(parsedActionId, cancellationToken);
                return JsonSerializer.Serialize(new { actionId = parsedActionId, canceled }, JsonOptions);
            },
            cancellationToken);
    }

    [KernelFunction(ToolCreateMemoryRelationship)]
    [Description("Create or update a relationship edge between two memory nodes.")]
    public async Task<string> CreateMemoryRelationshipAsync(
        [Description("Session id")] string sessionId,
        [Description("From node type: SemanticClaim|EpisodicEvent|ProceduralRoutine|SelfPreference|ScheduledAction|SubconsciousDebate|ToolInvocation")] string fromType,
        [Description("From node id")] string fromId,
        [Description("To node type: SemanticClaim|EpisodicEvent|ProceduralRoutine|SelfPreference|ScheduledAction|SubconsciousDebate|ToolInvocation")] string toType,
        [Description("To node id")] string toId,
        [Description("Relationship type, e.g. supports, contradicts, superseded_by, about")] string relationshipType,
        [Description("Confidence in [0,1]")] double confidence = 0.7,
        [Description("Strength in [0,1]")] double strength = 0.7,
        CancellationToken cancellationToken = default)
    {
        var normalizedSessionId = RequireNonEmpty(sessionId, nameof(sessionId));
        var normalizedFromId = RequireNonEmpty(fromId, nameof(fromId));
        var normalizedToId = RequireNonEmpty(toId, nameof(toId));
        var normalizedRelationshipType = RequireNonEmpty(relationshipType, nameof(relationshipType)).ToLowerInvariant();
        if (!Enum.TryParse<MemoryNodeType>(fromType?.Trim(), true, out var parsedFromType))
        {
            throw new ArgumentException("fromType is invalid.");
        }

        if (!Enum.TryParse<MemoryNodeType>(toType?.Trim(), true, out var parsedToType))
        {
            throw new ArgumentException("toType is invalid.");
        }

        return await ExecuteAsync(
            ToolCreateMemoryRelationship,
            isWrite: true,
            new
            {
                sessionId = normalizedSessionId,
                fromType = parsedFromType,
                fromId = normalizedFromId,
                toType = parsedToType,
                toId = normalizedToId,
                relationshipType = normalizedRelationshipType,
                confidence,
                strength
            },
            async () =>
            {
                var created = await memoryRelationshipRepository.UpsertAsync(
                    new MemoryRelationship(
                        Guid.NewGuid(),
                        normalizedSessionId,
                        parsedFromType,
                        normalizedFromId,
                        parsedToType,
                        normalizedToId,
                        normalizedRelationshipType,
                        Math.Clamp(confidence, 0, 1),
                        Math.Clamp(strength, 0, 1),
                        MemoryRelationshipStatus.Active,
                        null,
                        null,
                        null,
                        DateTimeOffset.UtcNow,
                        DateTimeOffset.UtcNow),
                    cancellationToken);

                return JsonSerializer.Serialize(created, JsonOptions);
            },
            cancellationToken);
    }

    [KernelFunction(ToolGetMemoryRelationships)]
    [Description("Get memory relationships for a session, optionally filtered by node and type.")]
    public async Task<string> GetMemoryRelationshipsAsync(
        [Description("Session id")] string sessionId,
        [Description("Optional node type")] string? nodeType = null,
        [Description("Optional node id")] string? nodeId = null,
        [Description("Optional relationship type")] string? relationshipType = null,
        [Description("Maximum rows")] int take = 100,
        CancellationToken cancellationToken = default)
    {
        var normalizedSessionId = RequireNonEmpty(sessionId, nameof(sessionId));
        var boundedTake = Math.Clamp(take, 1, 500);
        return await ExecuteAsync(
            ToolGetMemoryRelationships,
            isWrite: false,
            new { sessionId = normalizedSessionId, nodeType, nodeId, relationshipType, take = boundedTake },
            async () =>
            {
                IReadOnlyList<MemoryRelationship> rows;
                if (!string.IsNullOrWhiteSpace(nodeType)
                    && !string.IsNullOrWhiteSpace(nodeId)
                    && Enum.TryParse<MemoryNodeType>(nodeType.Trim(), true, out var parsedNodeType))
                {
                    rows = await memoryRelationshipRepository.QueryByNodeAsync(
                        normalizedSessionId,
                        parsedNodeType,
                        nodeId.Trim(),
                        relationshipType,
                        boundedTake,
                        cancellationToken);
                }
                else
                {
                    rows = await memoryRelationshipRepository.QueryBySessionAsync(
                        normalizedSessionId,
                        relationshipType,
                        MemoryRelationshipStatus.Active,
                        boundedTake,
                        cancellationToken);
                }

                return JsonSerializer.Serialize(
                    new
                    {
                        sessionId = normalizedSessionId,
                        count = rows.Count,
                        relationships = rows
                    },
                    JsonOptions);
            },
            cancellationToken);
    }

    [KernelFunction(ToolBackfillMemoryRelationships)]
    [Description("Run relationship backfill from existing memory records.")]
    public async Task<string> BackfillMemoryRelationshipsAsync(
        [Description("Optional session id filter")] string? sessionId = null,
        [Description("Max scan size")] int take = 2000,
        CancellationToken cancellationToken = default)
    {
        var normalizedSessionId = string.IsNullOrWhiteSpace(sessionId) ? null : sessionId.Trim();
        return await ExecuteAsync(
            ToolBackfillMemoryRelationships,
            isWrite: true,
            new { sessionId = normalizedSessionId, take },
            async () =>
            {
                var result = await memoryRelationshipRepository.BackfillAsync(normalizedSessionId, Math.Clamp(take, 100, 10000), cancellationToken);
                return JsonSerializer.Serialize(result, JsonOptions);
            },
            cancellationToken);
    }

    [KernelFunction(ToolExtractMemoryRelationships)]
    [Description("Use the mini model to infer memory relationships for a session. Set apply=false for dry-run.")]
    public async Task<string> ExtractMemoryRelationshipsAsync(
        [Description("Session id")] string sessionId,
        [Description("Maximum candidates to scan")] int take = 200,
        [Description("Whether to apply inferred relationships")] bool apply = true,
        CancellationToken cancellationToken = default)
    {
        var normalizedSessionId = RequireNonEmpty(sessionId, nameof(sessionId));
        return await ExecuteAsync(
            ToolExtractMemoryRelationships,
            isWrite: apply,
            new { sessionId = normalizedSessionId, take, apply },
            async () =>
            {
                var result = await memoryRelationshipExtractionService.ExtractAsync(
                    normalizedSessionId,
                    Math.Clamp(take, 20, 2000),
                    apply,
                    cancellationToken);
                return JsonSerializer.Serialize(result, JsonOptions);
            },
            cancellationToken);
    }

    private async Task<string> StoreWorkingAsync(string sessionId, string memoryText, StoreRoute route, CancellationToken cancellationToken)
    {
        var current = await workingMemoryStore.GetAsync(sessionId, cancellationToken);
        var role = NormalizeRole(route.Key) ?? "assistant";
        var content = string.IsNullOrWhiteSpace(route.Value) ? memoryText : route.Value.Trim();
        var updated = current.Turns
            .Concat([new WorkingMemoryTurn(role, content, DateTimeOffset.UtcNow)])
            .TakeLast(20)
            .ToArray();
        var next = new WorkingMemoryContext(sessionId, updated);
        await workingMemoryStore.SaveAsync(next, cancellationToken);

        return JsonSerializer.Serialize(
            new
            {
                layer = "working",
                role,
                stored = content,
                totalTurns = updated.Length
            },
            JsonOptions);
    }

    private async Task<string> StoreEpisodicAsync(string sessionId, string memoryText, StoreRoute route, CancellationToken cancellationToken)
    {
        var who = string.IsNullOrWhiteSpace(route.Key) ? "user" : route.Key.Trim();
        var what = string.IsNullOrWhiteSpace(route.Value) ? memoryText : route.Value.Trim();
        var memoryEvent = new EpisodicMemoryEvent(
            Guid.NewGuid(),
            sessionId,
            who,
            what,
            DateTimeOffset.UtcNow,
            "store_memory:auto",
            "tool:store_memory");
        await episodicMemoryRepository.AppendAsync(memoryEvent, cancellationToken);

        return JsonSerializer.Serialize(
            new
            {
                layer = "episodic",
                memoryEvent.EventId,
                memoryEvent.Who,
                memoryEvent.What,
                memoryEvent.OccurredAt
            },
            JsonOptions);
    }

    private async Task<string> StoreSemanticAsync(string sessionId, string memoryText, StoreRoute route, CancellationToken cancellationToken)
    {
        var defaultSubject = $"session:{sessionId}";
        var rawKey = route.Key?.Trim();
        var subject = string.IsNullOrWhiteSpace(rawKey) ? defaultSubject : rawKey;
        var predicate = string.IsNullOrWhiteSpace(route.Predicate) ? "states" : route.Predicate.Trim();
        if (!subject.StartsWith("session:", StringComparison.OrdinalIgnoreCase)
            && !subject.StartsWith("companion:", StringComparison.OrdinalIgnoreCase))
        {
            // Keep semantic writes within companion scope by using session subject.
            predicate = string.IsNullOrWhiteSpace(rawKey) ? predicate : rawKey;
            subject = defaultSubject;
        }
        var value = string.IsNullOrWhiteSpace(route.Value) ? memoryText : route.Value.Trim();
        var confidence = Math.Clamp(route.Confidence ?? 0.72, 0, 1);
        var companionId = await companionScopeResolver.ResolveCompanionIdOrThrowAsync(sessionId, cancellationToken);
        var existingActive = await semanticMemoryRepository.QueryClaimsAsync(
            companionId,
            subject: subject,
            predicate: predicate,
            status: SemanticClaimStatus.Active,
            take: 200,
            cancellationToken: cancellationToken);
        var duplicate = existingActive.FirstOrDefault(
            x => x.Subject.Equals(subject, StringComparison.OrdinalIgnoreCase)
                 && x.Predicate.Equals(predicate, StringComparison.OrdinalIgnoreCase)
                 && x.Value.Equals(value, StringComparison.OrdinalIgnoreCase));
        if (duplicate is not null)
        {
            logger.LogInformation(
                "Semantic memory deduplicated. Subject={Subject} Predicate={Predicate} Value={Value}",
                TruncateForLog(subject, 120),
                TruncateForLog(predicate, 120),
                TruncateForLog(value, 120));
            return JsonSerializer.Serialize(
                new
                {
                    layer = "semantic",
                    claim = duplicate,
                    deduplicated = true
                },
                JsonOptions);
        }

        var now = DateTimeOffset.UtcNow;
        var claim = new SemanticClaim(
            Guid.NewGuid(),
            subject,
            predicate,
            value,
            confidence,
            "global",
            SemanticClaimStatus.Active,
            null,
            null,
            null,
            now,
            now);

        var created = await semanticMemoryRepository.CreateClaimAsync(companionId, claim, cancellationToken);
        return JsonSerializer.Serialize(new { layer = "semantic", claim = created, deduplicated = false }, JsonOptions);
    }

    private async Task<string> StoreProceduralAsync(string sessionId, string memoryText, StoreRoute route, CancellationToken cancellationToken)
    {
        var companionId = await companionScopeResolver.ResolveCompanionIdOrThrowAsync(sessionId, cancellationToken);
        var trigger = string.IsNullOrWhiteSpace(route.Key) ? BuildTrigger(memoryText) : route.Key.Trim();
        var name = string.IsNullOrWhiteSpace(route.Predicate) ? "auto routine" : route.Predicate.Trim();
        var value = string.IsNullOrWhiteSpace(route.Value) ? memoryText : route.Value.Trim();
        var steps = BuildProcedureSteps(value);

        var routine = await proceduralMemoryRepository.UpsertAsync(
            companionId,
            new ProceduralRoutine(
                Guid.NewGuid(),
                trigger,
                name,
                steps,
                [],
                value,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow),
            cancellationToken);

        return JsonSerializer.Serialize(new { layer = "procedural", routine }, JsonOptions);
    }

    private async Task<string> StoreSelfModelAsync(string sessionId, string memoryText, StoreRoute route, CancellationToken cancellationToken)
    {
        var companionId = await companionScopeResolver.ResolveCompanionIdOrThrowAsync(sessionId, cancellationToken);
        var snapshot = await selfModelRepository.GetAsync(companionId, cancellationToken);
        var existingByKey = snapshot.Preferences
            .ToDictionary(x => NormalizeSelfKey(x.Key), StringComparer.Ordinal);

        var parsedEntries = ParseStructuredSelfEntries(memoryText)
            .DistinctBy(x => NormalizeSelfKey(x.Key))
            .ToArray();
        var entries = parsedEntries.Length > 0
            ? parsedEntries
            : [new SelfEntry(
                string.IsNullOrWhiteSpace(route.Key) ? DeriveSelfKey(memoryText) : route.Key,
                string.IsNullOrWhiteSpace(route.Value) ? DeriveSelfValue(memoryText) : route.Value.Trim())];

        var persistedEntries = new List<object>(entries.Length);
        var replacedCount = 0;
        foreach (var entry in entries)
        {
            var key = NormalizeSelfKey(entry.Key);
            var value = entry.Value.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            existingByKey.TryGetValue(key, out var existing);
            var replaced = existing is not null && !existing.Value.Equals(value, StringComparison.OrdinalIgnoreCase);
            await selfModelRepository.SetPreferenceAsync(companionId, key, value, cancellationToken);
            existingByKey[key] = new SelfPreference(key, value, DateTimeOffset.UtcNow);
            if (replaced)
            {
                replacedCount++;
                logger.LogInformation(
                    "Self-model preference updated. Key={Key} PreviousValue={PreviousValue} NewValue={NewValue}",
                    key,
                    TruncateForLog(existing!.Value, 120),
                    TruncateForLog(value, 120));
            }

            persistedEntries.Add(
                new
                {
                    key,
                    value,
                    stored = true,
                    replacedPrevious = replaced,
                    previousValue = replaced ? existing!.Value : null
                });
        }

        if (persistedEntries.Count == 0)
        {
            var fallbackKey = string.IsNullOrWhiteSpace(route.Key)
                ? DeriveSelfKey(memoryText)
                : NormalizeSelfKey(route.Key);
            var fallbackValue = string.IsNullOrWhiteSpace(route.Value) ? DeriveSelfValue(memoryText) : route.Value.Trim();
            await selfModelRepository.SetPreferenceAsync(companionId, fallbackKey, fallbackValue, cancellationToken);
            persistedEntries.Add(
                new
                {
                    key = fallbackKey,
                    value = fallbackValue,
                    stored = true,
                    replacedPrevious = false,
                    previousValue = (string?)null
                });
        }

        return JsonSerializer.Serialize(
            new
            {
                layer = "self",
                storedCount = persistedEntries.Count,
                replacedCount,
                entries = persistedEntries
            },
            JsonOptions);
    }

    private async Task<IReadOnlyList<StoreRoute>> ResolveStoreRoutesAsync(
        string sessionId,
        string memoryText,
        string? hint,
        CancellationToken cancellationToken)
    {
        RegisterRoutingPluginsIfNeeded();
        var hintedLayer = InferStoreLayerFromHint(hint);
        if (hintedLayer is not null && hintedLayer is not ("auto" or "all"))
        {
            logger.LogInformation("store_memory routing honored hint. Hint={Hint} Layer={Layer}", hint, hintedLayer);
            return
            [
                new StoreRoute
                {
                    Layer = hintedLayer,
                    Value = memoryText,
                    Confidence = 0.95
                }
            ];
        }

        var prompt =
            "You are the memory-write routing model." + Environment.NewLine +
            "Decide what to store and where to store it." + Environment.NewLine +
            "Route this memory write into one or more entries across: working, episodic, semantic, procedural, self." + Environment.NewLine +
            "Return strict JSON only in one of these forms:" + Environment.NewLine +
            "1) {\"entries\":[{\"layer\":\"...\",\"key\":\"...\",\"predicate\":\"...\",\"value\":\"...\",\"confidence\":0.0}]}" + Environment.NewLine +
            "2) {\"layer\":\"...\",\"key\":\"...\",\"predicate\":\"...\",\"value\":\"...\",\"confidence\":0.0}" + Environment.NewLine +
            "Available tools:" + Environment.NewLine +
            "- retrieve_memory(sessionId, query, take?, layer?)" + Environment.NewLine +
            "- get_current_time(utcOffset?)" + Environment.NewLine +
            "- get_memory_relationships(sessionId, nodeType?, nodeId?, relationshipType?, take?)" + Environment.NewLine +
            "Layer definitions:" + Environment.NewLine +
            "- working: short-lived session context." + Environment.NewLine +
            "- episodic: timestamped events/experiences." + Environment.NewLine +
            "- semantic: durable general facts/claims." + Environment.NewLine +
            "- procedural: reusable steps/routines." + Environment.NewLine +
            "- self: assistant identity/profile/preferences." + Environment.NewLine +
            "Rules:" + Environment.NewLine +
            "- Before routing, call tools when verification is needed." + Environment.NewLine +
            "- If the memory references existing facts/identity/history/time, retrieve/verify first before deciding entries." + Environment.NewLine +
            "- Do not assume; check memory when routing depends on prior state or possible conflicts." + Environment.NewLine +
            "- Decompose compound memory into multiple entries when it contains multiple distinct facts." + Environment.NewLine +
            "- Assistant identity/profile facts (name, DOB, role, origin, preferences, creators, purpose) -> self." + Environment.NewLine +
            "- Durable facts -> semantic." + Environment.NewLine +
            "- Time-bound event logs -> episodic." + Environment.NewLine +
            "- Reusable step patterns -> procedural." + Environment.NewLine +
            "- Short-lived context -> working." + Environment.NewLine +
            "- If Hint clearly maps to a layer, follow the Hint unless MemoryText strongly contradicts it." + Environment.NewLine +
            "For self layer, use stable keys when possible: identity.name, identity.birth_datetime, identity.role, identity.origin." + Environment.NewLine +
            "Do not output markdown." + Environment.NewLine +
            $"SessionId: {sessionId}" + Environment.NewLine +
            $"Hint: {hint ?? string.Empty}" + Environment.NewLine +
            $"MemoryText: {memoryText}";

        try
        {
            var executionSettings = GetRoutingExecutionSettings();
            var result = executionSettings is null
                ? await routingKernel.Value.InvokePromptAsync(prompt, cancellationToken: cancellationToken)
                : await routingKernel.Value.InvokePromptAsync(prompt, new KernelArguments(executionSettings), cancellationToken: cancellationToken);
            var raw = result.GetValue<string>();

            var batch = DeserializeLenient<StoreRouteBatch>(raw);
            if (batch?.Entries is { Length: > 0 })
            {
                var normalizedEntries = batch.Entries
                    .Select(entry => NormalizeStoreRoute(entry, memoryText))
                    .Where(static entry => entry is not null)
                    .Cast<StoreRoute>()
                    .ToArray();
                if (normalizedEntries.Length > 0)
                {
                    logger.LogInformation("store_memory router returned multi-entry plan. Count={Count}", normalizedEntries.Length);
                    return normalizedEntries;
                }
            }

            var single = DeserializeLenient<StoreRoute>(raw);
            var normalizedSingle = NormalizeStoreRoute(single, memoryText);
            if (normalizedSingle is not null)
            {
                logger.LogInformation("store_memory router returned single-entry plan. Layer={Layer}", normalizedSingle.Layer);
                return [normalizedSingle];
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Memory routing model failed for store_memory. Falling back to heuristics.");
        }

        return
        [
            new StoreRoute
            {
                Layer = InferStoreLayerHeuristically(memoryText),
                Value = memoryText,
                Confidence = 0.72
            }
        ];
    }

    private async Task<string> PersistStoreRouteAsync(
        string sessionId,
        string memoryText,
        StoreRoute route,
        CancellationToken cancellationToken)
    {
        var layer = NormalizeLayer(route.Layer) ?? InferStoreLayerHeuristically(memoryText);
        logger.LogInformation(
            "store_memory persisting route. SessionId={SessionId} Layer={Layer} Key={Key} Predicate={Predicate}",
            sessionId,
            layer,
            route.Key,
            route.Predicate);
        return layer switch
        {
            "self" => await StoreSelfModelAsync(sessionId, memoryText, route, cancellationToken),
            "semantic" => await StoreSemanticAsync(sessionId, memoryText, route, cancellationToken),
            "episodic" => await StoreEpisodicAsync(sessionId, memoryText, route, cancellationToken),
            "procedural" => await StoreProceduralAsync(sessionId, memoryText, route, cancellationToken),
            _ => await StoreWorkingAsync(sessionId, memoryText, route, cancellationToken)
        };
    }

    private static StoreRoute? NormalizeStoreRoute(StoreRoute? route, string memoryText)
    {
        if (route is null)
        {
            return null;
        }

        var layer = NormalizeLayer(route.Layer);
        if (layer is null or "all" or "auto")
        {
            return null;
        }

        var value = string.IsNullOrWhiteSpace(route.Value) ? memoryText : route.Value.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return new StoreRoute
        {
            Layer = layer,
            Key = route.Key,
            Predicate = route.Predicate,
            Value = value,
            Confidence = route.Confidence
        };
    }

    private static object? ParseJsonOrString(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        try
        {
            return JsonSerializer.Deserialize<object>(value, JsonOptions);
        }
        catch
        {
            return value;
        }
    }

    private async Task<IReadOnlyList<string>> ResolveRetrieveLayersAsync(
        string query,
        string? explicitLayer,
        CompanionCognitiveProfileDocument profile,
        CancellationToken cancellationToken)
    {
        var normalizedExplicitLayer = NormalizeLayer(explicitLayer);
        if (!string.IsNullOrWhiteSpace(normalizedExplicitLayer) && normalizedExplicitLayer != "auto")
        {
            return normalizedExplicitLayer == "all"
                ? SortLayersByProfile(["working", "episodic", "semantic", "procedural", "self"], profile)
                : [normalizedExplicitLayer];
        }

        if (LooksLikeIdentityFieldQuery(query))
        {
            // Identity lookups can span self-model keys and semantic claims.
            var identityLayers = profile.Memory.LayerPriorities.IdentityBoost >= 0.5
                ? new[] { "self", "semantic" }
                : new[] { "semantic", "self" };
            return SortLayersByProfile(identityLayers, profile);
        }

        var prompt =
            "Select the most relevant memory layers for this retrieval query." + Environment.NewLine +
            "Valid layers: working, episodic, semantic, procedural, self." + Environment.NewLine +
            "Return strict JSON with key layers (array of 1-3 layer strings)." + Environment.NewLine +
            "Do not output markdown." + Environment.NewLine +
            $"Query: {query}";

        try
        {
            var result = await routingKernel.Value.InvokePromptAsync(prompt, cancellationToken: cancellationToken);
            var raw = result.GetValue<string>();
            var parsed = DeserializeLenient<RetrieveRoute>(raw);
            if (parsed?.Layers is { Length: > 0 })
            {
                var normalized = parsed.Layers
                    .Select(NormalizeLayer)
                    .Where(x => x is "working" or "episodic" or "semantic" or "procedural" or "self")
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Cast<string>()
                    .ToArray();

                if (normalized.Length > 0)
                {
                    return SortLayersByProfile(normalized, profile);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Memory routing model failed for retrieve_memory. Falling back to heuristics.");
        }

        return SortLayersByProfile(InferRetrieveLayersHeuristically(query), profile);
    }

    private async Task<ResolvedCompanionCognitiveProfile> ResolveCognitiveProfileAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await cognitiveProfileResolver.ResolveBySessionIdAsync(sessionId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Companion cognitive profile resolution failed. Falling back to defaults.");
            var fallback = new CompanionCognitiveProfileDocument();
            return new ResolvedCompanionCognitiveProfile(
                Guid.Empty,
                Guid.Empty,
                0,
                fallback,
                new CompanionCognitiveRuntimePolicy(Guid.Empty, Guid.Empty, 0, fallback, new RuntimeLimits(120, 20, 8, 1)),
                IsFallback: true);
        }
    }

    private static string[] SortLayersByProfile(IEnumerable<string> layers, CompanionCognitiveProfileDocument profile)
    {
        return layers
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(layer => GetLayerPriority(layer, profile))
            .ThenBy(layer => layer)
            .ToArray();
    }

    private static double GetLayerPriority(string layer, CompanionCognitiveProfileDocument profile)
    {
        return NormalizeLayer(layer) switch
        {
            "working" => profile.Memory.LayerPriorities.Working,
            "episodic" => profile.Memory.LayerPriorities.Episodic,
            "semantic" => profile.Memory.LayerPriorities.Semantic,
            "procedural" => profile.Memory.LayerPriorities.Procedural,
            "self" => profile.Memory.LayerPriorities.Self + profile.Memory.LayerPriorities.IdentityBoost,
            _ => 0
        };
    }

    private static QueryProfile BuildQueryProfile(string rawQuery, int take)
    {
        var normalizedQuery = rawQuery?.Trim() ?? string.Empty;
        var criteria = BuildSearchCriteria(normalizedQuery);
        return new QueryProfile(
            normalizedQuery,
            criteria,
            LooksLikeIdentityFieldQuery(normalizedQuery),
            Math.Clamp(take, 1, 100));
    }

    private async Task<LayerCandidateBatch> RetrieveLayerCandidatesAsync(
        string layer,
        string sessionId,
        QueryProfile profile,
        CancellationToken cancellationToken)
    {
        PruneRequestCachesIfNeeded();
        var cacheKey = $"{sessionId}|{layer}|{profile.RawQuery}|{profile.Take}";
        var wasCached = layerCandidateCache.TryGetValue(cacheKey, out var cached);
        if (wasCached && cached is not null)
        {
            var batch = await AwaitCachedLayerBatchAsync(cacheKey, cached);
            return batch with { FromCache = true };
        }

        var created = new Lazy<Task<LayerCandidateBatch>>(
            () => LoadLayerCandidatesAsync(layer, sessionId, profile, cancellationToken),
            LazyThreadSafetyMode.ExecutionAndPublication);
        var winner = layerCandidateCache.GetOrAdd(cacheKey, created);
        var fromCache = !ReferenceEquals(winner, created);
        var resolved = await AwaitCachedLayerBatchAsync(cacheKey, winner);
        return resolved with { FromCache = fromCache };
    }

    private async Task<LayerCandidateBatch> AwaitCachedLayerBatchAsync(
        string cacheKey,
        Lazy<Task<LayerCandidateBatch>> lazyTask)
    {
        try
        {
            return await lazyTask.Value;
        }
        catch
        {
            layerCandidateCache.TryRemove(cacheKey, out _);
            throw;
        }
    }

    private async Task<LayerCandidateBatch> LoadLayerCandidatesAsync(
        string layer,
        string sessionId,
        QueryProfile profile,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        LayerCandidateBatch BuildBatch(IReadOnlyList<RetrievalCandidate> candidates)
            => new(
                layer,
                candidates,
                Math.Max(0, (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds),
                false);

        switch (layer)
        {
            case "working":
                var working = await workingMemoryStore.GetAsync(sessionId, cancellationToken);
                var workingCandidates = working.Turns
                    .TakeLast(Math.Min(200, profile.Take * 6))
                    .Select(
                        turn => new RetrievalCandidate(
                            "working",
                            $"{turn.Role} {turn.Content}",
                            turn,
                            turn.CreatedAtUtc,
                            turn.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ? 1 : 0))
                    .ToArray();
                return BuildBatch(workingCandidates);

            case "episodic":
                var episodicTake = Math.Min(400, profile.Take * 8);
                var episodic = string.IsNullOrWhiteSpace(profile.RawQuery)
                    ? await episodicMemoryRepository.QueryBySessionAsync(
                        sessionId,
                        take: episodicTake,
                        cancellationToken: cancellationToken)
                    : await episodicMemoryRepository.SearchBySessionAsync(
                        sessionId,
                        profile.RawQuery,
                        take: episodicTake,
                        cancellationToken: cancellationToken);
                var episodicCandidates = episodic
                    .Select(
                        item => new RetrievalCandidate(
                            "episodic",
                            $"{item.Who} {item.What} {item.Context} {item.SourceReference}",
                            item,
                            item.OccurredAt,
                            2))
                    .ToArray();
                return BuildBatch(episodicCandidates);

            case "semantic":
                var semantic = await QuerySemanticCandidatesAsync(sessionId, profile, cancellationToken);
                var semanticCandidates = semantic
                    .Select(
                        item => new RetrievalCandidate(
                            "semantic",
                            $"{item.Subject} {item.Predicate} {item.Value} {item.Scope}",
                            item,
                            item.UpdatedAtUtc,
                            2))
                    .ToArray();
                return BuildBatch(semanticCandidates);

            case "procedural":
                var proceduralCompanionId = await companionScopeResolver.ResolveCompanionIdOrThrowAsync(sessionId, cancellationToken);
                var proceduralTake = Math.Min(200, profile.Take * 6);
                var procedural = string.IsNullOrWhiteSpace(profile.RawQuery)
                    ? await proceduralMemoryRepository.QueryRecentAsync(proceduralCompanionId, proceduralTake, cancellationToken)
                    : await proceduralMemoryRepository.SearchAsync(proceduralCompanionId, profile.RawQuery, proceduralTake, cancellationToken);
                var proceduralCandidates = procedural
                    .Select(
                        item => new RetrievalCandidate(
                            "procedural",
                            $"{item.Trigger} {item.Name} {string.Join(' ', item.Steps)} {item.Outcome}",
                            item,
                            item.UpdatedAtUtc,
                            1))
                    .ToArray();
                return BuildBatch(proceduralCandidates);

            case "self":
                var selfCompanionId = await companionScopeResolver.ResolveCompanionIdOrThrowAsync(sessionId, cancellationToken);
                var snapshot = await selfModelRepository.GetAsync(selfCompanionId, cancellationToken);
                var selfCandidates = snapshot.Preferences
                    .Take(Math.Min(200, profile.Take * 8))
                    .Select(
                        item => new RetrievalCandidate(
                            "self",
                            $"{item.Key} {item.Value}",
                            item,
                            item.UpdatedAtUtc,
                            profile.IsIdentityLike ? 4 : 1))
                    .ToArray();
                return BuildBatch(selfCandidates);

            default:
                return BuildBatch([]);
        }
    }

    private static IReadOnlyList<RankedCandidate> RankCandidates(
        IEnumerable<RetrievalCandidate> candidates,
        QueryProfile profile,
        IReadOnlyDictionary<Guid, int> semanticRelationshipDegree,
        CompanionCognitiveProfileDocument cognitiveProfile)
    {
        return candidates
            .Select(candidate => new RankedCandidate(candidate, ComputeCandidateScore(candidate, profile, semanticRelationshipDegree, cognitiveProfile)))
            .Where(x => string.IsNullOrWhiteSpace(profile.Criteria.NormalizedQuery) || x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Candidate.OccurredAtUtc ?? DateTimeOffset.MinValue)
            .ToArray();
    }

    private static IReadOnlyList<RankedCandidate> DeduplicateRankedCandidates(
        IReadOnlyList<RankedCandidate> ranked)
    {
        var deduped = new List<RankedCandidate>(ranked.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in ranked)
        {
            var key = $"{item.Candidate.Layer}:{NormalizeSearchText(item.Candidate.Text)}";
            if (seen.Add(key))
            {
                deduped.Add(item);
            }
        }

        return deduped;
    }

    private static Dictionary<string, object?> BuildLayerResults(
        IReadOnlyList<string> selectedLayers,
        IReadOnlyList<RankedCandidate> ranked,
        int take)
    {
        var boundedTake = Math.Clamp(take, 1, 100);
        var results = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var layer in selectedLayers)
        {
            var payloads = ranked
                .Where(x => x.Candidate.Layer.Equals(layer, StringComparison.OrdinalIgnoreCase))
                .Take(boundedTake)
                .Select(x => x.Candidate.Payload)
                .ToArray();
            results[layer] = payloads;
        }

        return results;
    }

    private static int ComputeCandidateScore(
        RetrievalCandidate candidate,
        QueryProfile profile,
        IReadOnlyDictionary<Guid, int> semanticRelationshipDegree,
        CompanionCognitiveProfileDocument cognitiveProfile)
    {
        var score = candidate.BaseScore;
        score += ScoreByStringSearch(candidate.Text, profile.Criteria);
        score += ComputeLayerPriorityBonus(candidate.Layer, profile.IsIdentityLike, cognitiveProfile);
        score += ComputeRecencyBonus(candidate.OccurredAtUtc, cognitiveProfile);
        score += ComputePayloadBonus(candidate, profile, semanticRelationshipDegree, cognitiveProfile);
        return score;
    }

    private static int ComputePayloadBonus(
        RetrievalCandidate candidate,
        QueryProfile profile,
        IReadOnlyDictionary<Guid, int> semanticRelationshipDegree,
        CompanionCognitiveProfileDocument cognitiveProfile)
    {
        var bonus = 0;
        var confidenceWeight = Math.Clamp(cognitiveProfile.Memory.RetrievalWeights.Confidence, 0, 1.5);
        var relationshipWeight = Math.Clamp(cognitiveProfile.Memory.RetrievalWeights.RelationshipDegree, 0, 1.5);
        var evidenceWeight = Math.Clamp(cognitiveProfile.Memory.RetrievalWeights.EvidenceStrength, 0, 1.5);
        if (candidate.Payload is SemanticClaim claim)
        {
            bonus += (int)Math.Round((claim.Status switch
            {
                SemanticClaimStatus.Active => 2,
                SemanticClaimStatus.Probabilistic => -1,
                SemanticClaimStatus.Superseded => -6,
                _ => 0
            }) * evidenceWeight);
            bonus += (int)Math.Round(Math.Clamp(claim.Confidence, 0, 1) * 4 * confidenceWeight);

            if (profile.IsIdentityLike
                && (ContainsAnyAlias(claim.Subject, profile.Criteria.Aliases)
                    || ContainsAnyAlias(claim.Predicate, profile.Criteria.Aliases)))
            {
                bonus += 3;
            }

            if (semanticRelationshipDegree.TryGetValue(claim.ClaimId, out var degree))
            {
                bonus += (int)Math.Round(Math.Clamp(degree, 0, 5) * relationshipWeight);
            }
        }

        if (candidate.Payload is SelfPreference pref
            && profile.IsIdentityLike
            && ContainsAnyAlias(pref.Key, profile.Criteria.Aliases))
        {
            bonus += 5;
        }

        return bonus;
    }

    private static bool ContainsAnyAlias(string value, IReadOnlyList<string> aliases)
    {
        var normalized = NormalizeSearchText(value);
        foreach (var alias in aliases)
        {
            if (!string.IsNullOrWhiteSpace(alias)
                && normalized.Contains(alias, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static int ComputeLayerPriorityBonus(string layer, bool isIdentityLike, CompanionCognitiveProfileDocument cognitiveProfile)
    {
        var layerBase = layer switch
        {
            "working" => cognitiveProfile.Memory.LayerPriorities.Working,
            "episodic" => cognitiveProfile.Memory.LayerPriorities.Episodic,
            "semantic" => cognitiveProfile.Memory.LayerPriorities.Semantic,
            "procedural" => cognitiveProfile.Memory.LayerPriorities.Procedural,
            "self" => cognitiveProfile.Memory.LayerPriorities.Self,
            _ => 0
        };
        var scaledBase = (int)Math.Round(Math.Clamp(layerBase, 0, 1) * 4);
        if (isIdentityLike)
        {
            var identityBoost = (int)Math.Round(Math.Clamp(cognitiveProfile.Memory.LayerPriorities.IdentityBoost, 0, 1) * 4);
            return layer switch
            {
                "self" => 2 + scaledBase + identityBoost,
                "semantic" => 1 + scaledBase + identityBoost,
                "working" => scaledBase,
                _ => scaledBase
            };
        }

        return scaledBase;
    }

    private static int ComputeRecencyBonus(DateTimeOffset? occurredAtUtc, CompanionCognitiveProfileDocument cognitiveProfile)
    {
        if (!occurredAtUtc.HasValue)
        {
            return 0;
        }

        var recencyWeight = Math.Clamp(cognitiveProfile.Memory.RetrievalWeights.Recency, 0, 1.5);
        var ageHours = (DateTimeOffset.UtcNow - occurredAtUtc.Value).TotalHours;
        if (ageHours <= 24)
        {
            return (int)Math.Round(3 * recencyWeight);
        }

        if (ageHours <= 24 * 7)
        {
            return (int)Math.Round(2 * recencyWeight);
        }

        if (ageHours <= 24 * 30)
        {
            return (int)Math.Round(recencyWeight);
        }

        return 0;
    }

    private static object BuildInsights(QueryProfile profile, IReadOnlyList<RankedCandidate> ranked)
    {
        var topLayer = ranked.FirstOrDefault()?.Candidate.Layer;
        if (!profile.IsIdentityLike)
        {
            return new
            {
                topLayer,
                hasConflicts = false
            };
        }

        var facts = ranked
            .Take(40)
            .Select(TryExtractIdentityFact)
            .Where(x => x is not null)
            .Cast<IdentityFact>()
            .ToArray();

        var identityProfile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var conflicts = new List<object>();
        foreach (var group in facts.GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            var ordered = group
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.UpdatedAtUtc ?? DateTimeOffset.MinValue)
                .ToArray();
            var best = ordered[0];
            identityProfile[group.Key] = best.Value;

            var distinctValues = ordered
                .Select(x => x.Value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (distinctValues.Length > 1)
            {
                conflicts.Add(new
                {
                    key = group.Key,
                    values = distinctValues
                });
            }
        }

        return new
        {
            topLayer,
            hasConflicts = conflicts.Count > 0,
            identityProfile,
            conflicts
        };
    }

    private static IdentityFact? TryExtractIdentityFact(RankedCandidate ranked)
    {
        if (ranked.Candidate.Payload is SelfPreference pref)
        {
            var key = NormalizeSelfKey(pref.Key);
            if (string.IsNullOrWhiteSpace(pref.Value))
            {
                return null;
            }

            return new IdentityFact(key, pref.Value, ranked.Candidate.Layer, ranked.Score, pref.UpdatedAtUtc);
        }

        if (ranked.Candidate.Payload is SemanticClaim claim)
        {
            var key = InferIdentityKeyFromSemanticClaim(claim);
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(claim.Value))
            {
                return null;
            }

            return new IdentityFact(key, claim.Value, ranked.Candidate.Layer, ranked.Score, claim.UpdatedAtUtc);
        }

        return null;
    }

    private void RegisterRoutingPluginsIfNeeded()
    {
        if (routingKernel.Value.Plugins.Any(p => string.Equals(p.Name, RoutingPluginName, StringComparison.Ordinal)))
        {
            return;
        }

        routingKernel.Value.Plugins.AddFromObject(new RoutingReadOnlyTools(this), RoutingPluginName);
    }

    private PromptExecutionSettings? GetRoutingExecutionSettings()
    {
        var routingProvider = string.IsNullOrWhiteSpace(semanticKernelOptions.ClaimExtractionProvider)
            ? semanticKernelOptions.Provider
            : semanticKernelOptions.ClaimExtractionProvider;

        if (string.Equals(routingProvider, "OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            return new OpenAIPromptExecutionSettings
            {
                ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
            };
        }

        if (string.Equals(routingProvider, "Ollama", StringComparison.OrdinalIgnoreCase))
        {
            return new OllamaPromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
            };
        }

        return null;
    }

    private static string? InferIdentityKeyFromSemanticClaim(SemanticClaim claim)
    {
        var subjectKey = InferIdentityKeyFromNormalized(NormalizeSearchText(claim.Subject));
        if (!string.IsNullOrWhiteSpace(subjectKey))
        {
            return subjectKey;
        }

        var predicateKey = InferIdentityKeyFromNormalized(NormalizeSearchText(claim.Predicate));
        if (!string.IsNullOrWhiteSpace(predicateKey))
        {
            return predicateKey;
        }

        return InferIdentityKeyFromNormalized(NormalizeSearchText($"{claim.Subject} {claim.Predicate}"));
    }

    private static string? InferIdentityKeyFromNormalized(string normalized)
    {
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (normalized.Contains("name", StringComparison.Ordinal))
        {
            return "identity.name";
        }

        if (normalized.Contains("birth", StringComparison.Ordinal)
            || normalized.Contains("born", StringComparison.Ordinal)
            || normalized.Contains("dob", StringComparison.Ordinal))
        {
            return "identity.birth_datetime";
        }

        if (normalized.Contains("role", StringComparison.Ordinal))
        {
            return "identity.role";
        }

        if (normalized.Contains("origin", StringComparison.Ordinal)
            || normalized.Contains("from", StringComparison.Ordinal))
        {
            return "identity.origin";
        }

        if (normalized.Contains("preference", StringComparison.Ordinal)
            || normalized.Contains("prefer", StringComparison.Ordinal))
        {
            return "identity.preference";
        }

        if (normalized.Contains("profile note", StringComparison.Ordinal))
        {
            return "identity.profile_note";
        }

        return null;
    }

    private async Task<IReadOnlyList<SemanticClaim>> QuerySemanticCandidatesAsync(
        string sessionId,
        QueryProfile profile,
        CancellationToken cancellationToken)
    {
        PruneRequestCachesIfNeeded();
        var companionId = await companionScopeResolver.ResolveCompanionIdOrThrowAsync(sessionId, cancellationToken);
        var expandedTake = Math.Min(240, profile.Take * 8);
        if (string.IsNullOrWhiteSpace(profile.RawQuery))
        {
            return await semanticMemoryRepository.QueryClaimsAsync(
                companionId,
                take: expandedTake,
                cancellationToken: cancellationToken);
        }

        var terms = profile.Criteria.Aliases
            .Concat(profile.Criteria.Terms)
            .Append(profile.Criteria.NormalizedQuery)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .Take(12)
            .ToArray();

        // Semantic repository uses EF Core; execute term lookups serially to avoid DbContext concurrency.
        var candidates = new List<SemanticClaim>(expandedTake * Math.Max(1, terms.Length));
        foreach (var term in terms)
        {
            var termClaims = await QuerySemanticByTermCachedAsync(companionId, term, expandedTake, cancellationToken);
            candidates.AddRange(termClaims);
        }

        if (candidates.Count < expandedTake)
        {
            var recent = await semanticMemoryRepository.QueryClaimsAsync(
                companionId,
                take: expandedTake,
                cancellationToken: cancellationToken);
            candidates.AddRange(recent);
        }

        return candidates
            .GroupBy(x => x.ClaimId)
            .Select(x => x.First())
            .ToArray();
    }

    private async Task<IReadOnlyList<SemanticClaim>> QuerySemanticByTermCachedAsync(
        Guid companionId,
        string term,
        int take,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"{companionId:N}|{term}|{take}";
        var wasCached = semanticTermCache.TryGetValue(cacheKey, out var cached);
        if (wasCached && cached is not null)
        {
            return await AwaitCachedSemanticTermAsync(cacheKey, cached);
        }

        var created = new Lazy<Task<IReadOnlyList<SemanticClaim>>>(
            () => QuerySemanticByTermAsync(companionId, term, take, cancellationToken),
            LazyThreadSafetyMode.ExecutionAndPublication);
        var winner = semanticTermCache.GetOrAdd(cacheKey, created);
        return await AwaitCachedSemanticTermAsync(cacheKey, winner);
    }

    private async Task<IReadOnlyList<SemanticClaim>> AwaitCachedSemanticTermAsync(
        string cacheKey,
        Lazy<Task<IReadOnlyList<SemanticClaim>>> lazyTask)
    {
        try
        {
            return await lazyTask.Value;
        }
        catch
        {
            semanticTermCache.TryRemove(cacheKey, out _);
            throw;
        }
    }

    private async Task<IReadOnlyList<SemanticClaim>> QuerySemanticByTermAsync(
        Guid companionId,
        string term,
        int take,
        CancellationToken cancellationToken)
    {
        return await semanticMemoryRepository.SearchClaimsAsync(
            companionId: companionId,
            query: term,
            take: take,
            cancellationToken: cancellationToken);
    }

    private void PruneRequestCachesIfNeeded()
    {
        if (layerCandidateCache.Count > MaxRequestCacheEntries)
        {
            layerCandidateCache.Clear();
        }

        if (semanticTermCache.Count > MaxRequestCacheEntries)
        {
            semanticTermCache.Clear();
        }
    }

    private async Task<string> ExecuteAsync(
        string toolName,
        bool isWrite,
        object args,
        Func<Task<string>> action,
        CancellationToken cancellationToken)
    {
        var argsJson = JsonSerializer.Serialize(args, JsonOptions);
        logger.LogInformation(
            "Memory tool execution started. ToolName={ToolName} IsWrite={IsWrite} Args={Args}",
            toolName,
            isWrite,
            TruncateForLog(argsJson));

        try
        {
            var resultJson = await action();
            logger.LogInformation(
                "Memory tool execution succeeded. ToolName={ToolName} IsWrite={IsWrite} Result={Result}",
                toolName,
                isWrite,
                TruncateForLog(resultJson));

            await TryPersistToolAuditAsync(
                new ToolInvocationAudit(
                    Guid.NewGuid(),
                    toolName,
                    isWrite,
                    argsJson,
                    resultJson,
                    true,
                    null,
                    DateTimeOffset.UtcNow),
                cancellationToken);

            return resultJson;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Memory tool execution failed. ToolName={ToolName} IsWrite={IsWrite} Args={Args}",
                toolName,
                isWrite,
                TruncateForLog(argsJson));

            await TryPersistToolAuditAsync(
                new ToolInvocationAudit(
                    Guid.NewGuid(),
                    toolName,
                    isWrite,
                    argsJson,
                    "{}",
                    false,
                    ex.Message,
                    DateTimeOffset.UtcNow),
                cancellationToken);

            throw;
        }
    }

    private async Task TryPersistToolAuditAsync(ToolInvocationAudit audit, CancellationToken cancellationToken)
    {
        try
        {
            await toolInvocationAuditRepository.AddAsync(audit, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug(
                "Memory tool audit persistence was canceled. ToolName={ToolName} AuditId={AuditId}",
                audit.ToolName,
                audit.AuditId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Memory tool audit persistence failed. ToolName={ToolName} AuditId={AuditId}",
                audit.ToolName,
                audit.AuditId);
        }
    }

    private static string DeriveSelfKey(string memoryText)
    {
        var lower = memoryText.ToLowerInvariant();
        if (lower.Contains("name"))
        {
            return "identity.name";
        }

        if (lower.Contains("born") || lower.Contains("birth"))
        {
            return "identity.birth_datetime";
        }

        if (lower.Contains("role"))
        {
            return "identity.role";
        }

        if (lower.Contains("origin") || lower.Contains("from"))
        {
            return "identity.origin";
        }

        if (lower.Contains("prefer"))
        {
            return "identity.preference";
        }

        return "identity.profile_note";
    }

    private static string NormalizeSelfKey(string rawKey)
    {
        var normalized = rawKey.Trim().ToLowerInvariant()
            .Replace(' ', '_')
            .Replace('-', '_')
            .Replace('/', '_');

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "identity.profile_note";
        }

        if (normalized.StartsWith("identity.", StringComparison.Ordinal))
        {
            return normalized;
        }

        return normalized switch
        {
            "name" => "identity.name",
            "birth" or "birthdate" or "birthday" or "dob" => "identity.birth_datetime",
            "role" => "identity.role",
            "origin" => "identity.origin",
            "preference" or "preferences" => "identity.preference",
            _ => $"identity.{normalized}"
        };
    }

    private static string DeriveSelfValue(string memoryText)
    {
        var patterns = new[]
        {
            "you were born",
            "your name is",
            "you are",
            "your role is"
        };

        foreach (var pattern in patterns)
        {
            var index = memoryText.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                var value = memoryText[(index + pattern.Length)..].Trim().TrimEnd('.', '!', '?');
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return memoryText.Trim();
    }

    private static IReadOnlyList<SelfEntry> ParseStructuredSelfEntries(string memoryText)
    {
        if (string.IsNullOrWhiteSpace(memoryText))
        {
            return [];
        }

        var text = memoryText.Trim();
        var firstEquals = text.IndexOf('=');
        var firstColon = text.IndexOf(':');
        if (firstEquals > 0 && firstColon >= 0 && firstColon < firstEquals)
        {
            text = text[(firstColon + 1)..].Trim();
        }

        // Some model outputs use sentence separators between key/value pairs.
        text = Regex.Replace(
            text,
            @"\.\s+(?=[a-zA-Z][a-zA-Z0-9_./-]*\s*(?:=|:))",
            "; ",
            RegexOptions.CultureInvariant);

        var likelyStructured = text.Contains('=') || text.Contains(':') || text.Contains(';') || text.Contains('\n');
        if (!likelyStructured)
        {
            return [];
        }

        var parsed = new List<SelfEntry>();
        var segments = text.Split([';', '\n', '\r', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var segment in segments)
        {
            var separatorIndex = segment.IndexOf('=');
            if (separatorIndex <= 0)
            {
                var colonIndex = segment.IndexOf(':');
                if (colonIndex > 0 && !segment.Contains("://", StringComparison.Ordinal))
                {
                    separatorIndex = colonIndex;
                }
            }

            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = segment[..separatorIndex].Trim().Trim('"', '\'', '`', '.', ',', ';');
            var value = segment[(separatorIndex + 1)..].Trim().Trim('"', '\'', '`', '.', ',', ';');
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            parsed.Add(new SelfEntry(key, value));
        }

        return parsed;
    }

    private static string BuildTrigger(string memoryText)
    {
        var trimmed = memoryText.Trim();
        return trimmed.Length <= 80 ? trimmed : trimmed[..80];
    }

    private static string[] BuildProcedureSteps(string value)
    {
        var byLine = value
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (byLine.Length > 1)
        {
            return byLine;
        }

        var byPipe = value
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (byPipe.Length > 1)
        {
            return byPipe;
        }

        var byThen = value
            .Split(" then ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (byThen.Length > 1)
        {
            return byThen;
        }

        return [value];
    }

    private static string InferStoreLayerHeuristically(string text)
    {
        var lower = text.ToLowerInvariant();
        if (lower.Contains("you are")
            || lower.Contains("your name")
            || lower.Contains("you were born")
            || lower.Contains("your role")
            || lower.Contains("your preference"))
        {
            return "self";
        }

        if (lower.Contains("step")
            || lower.Contains("workflow")
            || lower.Contains("procedure")
            || lower.Contains("always do"))
        {
            return "procedural";
        }

        if (lower.Contains("today")
            || lower.Contains("yesterday")
            || lower.Contains("at ")
            || lower.Contains("on "))
        {
            return "episodic";
        }

        return "semantic";
    }

    private static IReadOnlyList<string> InferRetrieveLayersHeuristically(string query)
    {
        var lower = query.ToLowerInvariant();
        if (lower.Contains("who are you")
            || lower.Contains("your name")
            || lower.Contains("born")
            || lower.Contains("about you")
            || lower.Contains("your role")
            || lower.Contains("identity."))
        {
            return ["self", "semantic"];
        }

        if (lower.Contains("remember")
            || lower.Contains("earlier")
            || lower.Contains("before")
            || lower.Contains("happened"))
        {
            return ["episodic", "working"];
        }

        if (lower.Contains("how")
            || lower.Contains("steps")
            || lower.Contains("routine")
            || lower.Contains("workflow"))
        {
            return ["procedural"];
        }

        if (lower.Contains("fact")
            || lower.Contains("know")
            || lower.Contains("status"))
        {
            return ["semantic"];
        }

        return ["working", "episodic", "semantic", "self"];
    }

    private static string? NormalizeLayer(string? rawLayer)
    {
        if (string.IsNullOrWhiteSpace(rawLayer))
        {
            return null;
        }

        var normalized = rawLayer.Trim().ToLowerInvariant();
        return normalized switch
        {
            "working" or "working-memory" => "working",
            "episodic" or "events" => "episodic",
            "semantic" or "claims" or "facts" => "semantic",
            "procedural" or "routines" => "procedural",
            "self" or "self-model" or "selfmodel" or "identity" => "self",
            "all" or "any" => "all",
            "auto" => "auto",
            _ => null
        };
    }

    private static string? InferStoreLayerFromHint(string? hint)
    {
        if (string.IsNullOrWhiteSpace(hint))
        {
            return null;
        }

        var normalized = hint.Trim().ToLowerInvariant();
        var direct = NormalizeLayer(normalized);
        if (direct is not null)
        {
            return direct;
        }

        var tokens = normalized
            .Split(['.', ':', '/', '|', ',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var token in tokens)
        {
            var layer = NormalizeLayer(token);
            if (layer is not null)
            {
                return layer;
            }
        }

        if (normalized.Contains("identity", StringComparison.Ordinal)
            || normalized.Contains("self", StringComparison.Ordinal))
        {
            return "self";
        }

        // Companion bootstrap hints should avoid model-routed storage paths so creation
        // remains resilient even when local routing/embedding services are unavailable.
        if (normalized.StartsWith("companion.", StringComparison.Ordinal))
        {
            return "self";
        }

        return null;
    }

    private static bool TryParseUtcOffset(string? input, out TimeSpan offset)
    {
        offset = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var trimmed = input.Trim();
        if (!Regex.IsMatch(trimmed, "^[+-][0-9]{2}:[0-9]{2}$", RegexOptions.CultureInvariant))
        {
            return false;
        }

        if (!TimeSpan.TryParseExact(trimmed[1..], "hh\\:mm", CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        offset = trimmed[0] == '-' ? -parsed : parsed;
        return offset >= TimeSpan.FromHours(-14) && offset <= TimeSpan.FromHours(14);
    }

    private static string? NormalizeRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return null;
        }

        var normalized = role.Trim().ToLowerInvariant();
        return normalized switch
        {
            "user" => "user",
            "assistant" => "assistant",
            _ => null
        };
    }

    private static string RequireNonEmpty(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{name} is required.");
        }

        return value.Trim();
    }

    private static string TruncateForLog(string value, int maxLength = 600)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return $"{value[..maxLength]}...(truncated)";
    }

    private static SearchCriteria BuildSearchCriteria(string query)
    {
        var normalizedQuery = NormalizeSearchText(query);
        var aliases = ExpandSearchAliases(query, normalizedQuery);
        var terms = TokenizeSearchTerms(normalizedQuery)
            .Concat(aliases.SelectMany(TokenizeSearchTerms))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new SearchCriteria(normalizedQuery, aliases, terms);
    }

    private static int ScoreByStringSearch(string value, SearchCriteria criteria)
    {
        var normalizedValue = NormalizeSearchText(value);
        if (string.IsNullOrWhiteSpace(normalizedValue))
        {
            return 0;
        }

        var score = 0;

        if (!string.IsNullOrWhiteSpace(criteria.NormalizedQuery)
            && normalizedValue.Contains(criteria.NormalizedQuery, StringComparison.Ordinal))
        {
            score += 6;
        }

        foreach (var alias in criteria.Aliases)
        {
            if (normalizedValue.Contains(alias, StringComparison.Ordinal))
            {
                score += 4;
            }
        }

        foreach (var term in criteria.Terms)
        {
            if (term.Length >= 3 && normalizedValue.Contains(term, StringComparison.Ordinal))
            {
                score += 1;
            }
        }

        return score;
    }

    private static bool LooksLikeIdentityFieldQuery(string query)
    {
        var normalized = NormalizeSearchText(query);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        return normalized.Contains("identity ", StringComparison.Ordinal)
               || normalized.Contains("who are you", StringComparison.Ordinal)
               || normalized.Contains("your name", StringComparison.Ordinal)
               || normalized.Contains("your role", StringComparison.Ordinal)
               || normalized.Contains("where are you from", StringComparison.Ordinal)
               || normalized.Equals("origin", StringComparison.Ordinal)
               || normalized.Equals("role", StringComparison.Ordinal)
               || normalized.Equals("birth", StringComparison.Ordinal)
               || normalized.Equals("name", StringComparison.Ordinal);
    }

    private static string[] ExpandSearchAliases(string rawQuery, string normalizedQuery)
    {
        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return [];
        }

        var aliases = new HashSet<string>(StringComparer.Ordinal)
        {
            normalizedQuery
        };

        var raw = rawQuery.Trim().ToLowerInvariant();
        if (raw.StartsWith("identity.", StringComparison.Ordinal))
        {
            aliases.Add(NormalizeSearchText(raw["identity.".Length..]));
        }

        if (normalizedQuery.StartsWith("identity ", StringComparison.Ordinal))
        {
            aliases.Add(normalizedQuery["identity ".Length..]);
        }

        var canonicalIdentityFields = new[]
        {
            "name",
            "birth",
            "birth datetime",
            "role",
            "origin",
            "preference",
            "profile note"
        };

        foreach (var field in canonicalIdentityFields)
        {
            if (normalizedQuery.Equals(field, StringComparison.Ordinal))
            {
                aliases.Add(NormalizeSearchText($"identity {field}"));
            }
        }

        aliases.RemoveWhere(string.IsNullOrWhiteSpace);
        return aliases.ToArray();
    }

    private static IEnumerable<string> TokenizeSearchTerms(string value)
    {
        return value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length >= 3);
    }

    private static string NormalizeSearchText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var chars = value
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : ' ')
            .ToArray();

        var collapsed = new string(chars);
        while (collapsed.Contains("  ", StringComparison.Ordinal))
        {
            collapsed = collapsed.Replace("  ", " ", StringComparison.Ordinal);
        }

        return collapsed.Trim();
    }

    private static T? DeserializeLenient<T>(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return default;
        }

        var candidate = raw.Trim();
        if (candidate.StartsWith("```", StringComparison.Ordinal))
        {
            candidate = candidate
                .Replace("```json", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("```", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Trim();
        }

        var start = candidate.IndexOf('{');
        var end = candidate.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            candidate = candidate[start..(end + 1)];
        }

        try
        {
            return JsonSerializer.Deserialize<T>(candidate, JsonOptions);
        }
        catch
        {
            return default;
        }
    }

    private sealed record QueryProfile(
        string RawQuery,
        SearchCriteria Criteria,
        bool IsIdentityLike,
        int Take);

    private sealed record SelfEntry(string Key, string Value);

    private sealed record RetrievalCandidate(
        string Layer,
        string Text,
        object Payload,
        DateTimeOffset? OccurredAtUtc,
        int BaseScore);

    private sealed record RankedCandidate(RetrievalCandidate Candidate, int Score);

    private sealed record LayerCandidateBatch(
        string Layer,
        IReadOnlyList<RetrievalCandidate> Candidates,
        double ElapsedMs,
        bool FromCache);

    private sealed record IdentityFact(
        string Key,
        string Value,
        string Layer,
        int Score,
        DateTimeOffset? UpdatedAtUtc);

    private sealed class StoreRoute
    {
        public string? Layer { get; set; }
        public string? Key { get; set; }
        public string? Predicate { get; set; }
        public string? Value { get; set; }
        public double? Confidence { get; set; }
    }

    private sealed class StoreRouteBatch
    {
        public StoreRoute[]? Entries { get; set; }
    }

    private sealed class RetrieveRoute
    {
        public string[]? Layers { get; set; }
    }

    private sealed class RoutingReadOnlyTools(MemoryToolsPlugin owner)
    {
        [KernelFunction(ToolRetrieveMemory)]
        [Description("Retrieve memory for this session. Read-only tool for routing context.")]
        public Task<string> RetrieveMemoryAsync(
            [Description("Session id")] string sessionId,
            [Description("What to retrieve")] string query,
            [Description("Maximum items to return")] int take = 20,
            [Description("Optional layer override: working|episodic|semantic|procedural|self|all")] string? layer = null,
            CancellationToken cancellationToken = default)
            => owner.RetrieveMemoryAsync(sessionId, query, take, layer, cancellationToken);

        [KernelFunction(ToolGetCurrentTime)]
        [Description("Get current time details. Optional UTC offset format: +/-HH:mm.")]
        public Task<string> GetCurrentTimeAsync(
            [Description("Optional UTC offset in format +/-HH:mm")] string? utcOffset = null,
            CancellationToken cancellationToken = default)
            => owner.GetCurrentTimeAsync(utcOffset, cancellationToken);
    }

    private sealed record SearchCriteria(string NormalizedQuery, string[] Aliases, string[] Terms);
}
