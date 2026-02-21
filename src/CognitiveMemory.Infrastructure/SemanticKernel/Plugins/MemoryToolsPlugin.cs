using System.ComponentModel;
using System.Collections.Concurrent;
using System.Text.Json;
using CognitiveMemory.Application.Abstractions;
using CognitiveMemory.Domain.Memory;
using CognitiveMemory.Infrastructure.SemanticKernel;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace CognitiveMemory.Infrastructure.SemanticKernel.Plugins;

public sealed class MemoryToolsPlugin(
    IWorkingMemoryStore workingMemoryStore,
    IEpisodicMemoryRepository episodicMemoryRepository,
    ISemanticMemoryRepository semanticMemoryRepository,
    IProceduralMemoryRepository proceduralMemoryRepository,
    ISelfModelRepository selfModelRepository,
    IToolInvocationAuditRepository toolInvocationAuditRepository,
    ClaimExtractionKernel routingKernel,
    ILogger<MemoryToolsPlugin> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string ToolStoreMemory = "store_memory";
    private const string ToolRetrieveMemory = "retrieve_memory";
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
                var route = await ResolveStoreRouteAsync(normalizedSessionId, normalizedText, normalizedHint, cancellationToken);
                var layer = NormalizeLayer(route.Layer) ?? InferStoreLayerHeuristically(normalizedText);

                return layer switch
                {
                    "self" => await StoreSelfModelAsync(normalizedText, route, cancellationToken),
                    "semantic" => await StoreSemanticAsync(normalizedSessionId, normalizedText, route, cancellationToken),
                    "episodic" => await StoreEpisodicAsync(normalizedSessionId, normalizedText, route, cancellationToken),
                    "procedural" => await StoreProceduralAsync(normalizedText, route, cancellationToken),
                    _ => await StoreWorkingAsync(normalizedSessionId, normalizedText, route, cancellationToken)
                };
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
                var queryProfile = BuildQueryProfile(normalizedQuery, normalizedTake);
                var selectedLayers = (await ResolveRetrieveLayersAsync(queryProfile.RawQuery, normalizedLayer, cancellationToken))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var startedAt = DateTimeOffset.UtcNow;

                var layerTasks = selectedLayers
                    .Select(layerName => RetrieveLayerCandidatesAsync(layerName, normalizedSessionId, queryProfile, cancellationToken))
                    .ToArray();
                var layerBatches = await Task.WhenAll(layerTasks);
                var candidates = layerBatches
                    .SelectMany(x => x.Candidates)
                    .ToArray();
                var ranked = DeduplicateRankedCandidates(RankCandidates(candidates, queryProfile))
                    .Take(Math.Max(queryProfile.Take * selectedLayers.Length, queryProfile.Take))
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
                        elapsedMs
                    }
                };

                return JsonSerializer.Serialize(payload, JsonOptions);
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
        var subject = string.IsNullOrWhiteSpace(route.Key) ? $"session:{sessionId}" : route.Key.Trim();
        var predicate = string.IsNullOrWhiteSpace(route.Predicate) ? "states" : route.Predicate.Trim();
        var value = string.IsNullOrWhiteSpace(route.Value) ? memoryText : route.Value.Trim();
        var confidence = Math.Clamp(route.Confidence ?? 0.72, 0, 1);
        var existingActive = await semanticMemoryRepository.QueryClaimsAsync(
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

        var created = await semanticMemoryRepository.CreateClaimAsync(claim, cancellationToken);
        return JsonSerializer.Serialize(new { layer = "semantic", claim = created, deduplicated = false }, JsonOptions);
    }

    private async Task<string> StoreProceduralAsync(string memoryText, StoreRoute route, CancellationToken cancellationToken)
    {
        var trigger = string.IsNullOrWhiteSpace(route.Key) ? BuildTrigger(memoryText) : route.Key.Trim();
        var name = string.IsNullOrWhiteSpace(route.Predicate) ? "auto routine" : route.Predicate.Trim();
        var value = string.IsNullOrWhiteSpace(route.Value) ? memoryText : route.Value.Trim();
        var steps = BuildProcedureSteps(value);

        var routine = await proceduralMemoryRepository.UpsertAsync(
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

    private async Task<string> StoreSelfModelAsync(string memoryText, StoreRoute route, CancellationToken cancellationToken)
    {
        var key = string.IsNullOrWhiteSpace(route.Key)
            ? DeriveSelfKey(memoryText)
            : NormalizeSelfKey(route.Key);
        var value = string.IsNullOrWhiteSpace(route.Value) ? DeriveSelfValue(memoryText) : route.Value.Trim();
        var snapshot = await selfModelRepository.GetAsync(cancellationToken);
        var existing = snapshot.Preferences.FirstOrDefault(x => NormalizeSelfKey(x.Key).Equals(key, StringComparison.Ordinal));
        var replaced = existing is not null
                       && !existing.Value.Equals(value, StringComparison.OrdinalIgnoreCase);
        await selfModelRepository.SetPreferenceAsync(key, value, cancellationToken);
        if (replaced)
        {
            logger.LogInformation(
                "Self-model preference updated. Key={Key} PreviousValue={PreviousValue} NewValue={NewValue}",
                key,
                TruncateForLog(existing!.Value, 120),
                TruncateForLog(value, 120));
        }

        return JsonSerializer.Serialize(
            new
            {
                layer = "self",
                key,
                value,
                stored = true,
                replacedPrevious = replaced,
                previousValue = replaced ? existing!.Value : null
            },
            JsonOptions);
    }

    private async Task<StoreRoute> ResolveStoreRouteAsync(
        string sessionId,
        string memoryText,
        string? hint,
        CancellationToken cancellationToken)
    {
        var prompt =
            "Route this memory write into exactly one layer: working, episodic, semantic, procedural, or self." + Environment.NewLine +
            "Return strict JSON with keys: layer, key, predicate, value, confidence." + Environment.NewLine +
            "Rules:" + Environment.NewLine +
            "- Assistant identity/profile facts (name, DOB, role, origin, preferences) -> self." + Environment.NewLine +
            "- Durable facts -> semantic." + Environment.NewLine +
            "- Time-bound event logs -> episodic." + Environment.NewLine +
            "- Reusable step patterns -> procedural." + Environment.NewLine +
            "- Short-lived context -> working." + Environment.NewLine +
            "For self layer, use stable keys when possible: identity.name, identity.birth_datetime, identity.role, identity.origin." + Environment.NewLine +
            "Do not output markdown." + Environment.NewLine +
            $"SessionId: {sessionId}" + Environment.NewLine +
            $"Hint: {hint ?? string.Empty}" + Environment.NewLine +
            $"MemoryText: {memoryText}";

        try
        {
            var result = await routingKernel.Value.InvokePromptAsync(prompt, cancellationToken: cancellationToken);
            var raw = result.GetValue<string>();
            var parsed = DeserializeLenient<StoreRoute>(raw);
            if (parsed is not null && NormalizeLayer(parsed.Layer) is not null)
            {
                return parsed;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Memory routing model failed for store_memory. Falling back to heuristics.");
        }

        return new StoreRoute
        {
            Layer = InferStoreLayerHeuristically(memoryText),
            Value = memoryText,
            Confidence = 0.6
        };
    }

    private async Task<IReadOnlyList<string>> ResolveRetrieveLayersAsync(
        string query,
        string? explicitLayer,
        CancellationToken cancellationToken)
    {
        var normalizedExplicitLayer = NormalizeLayer(explicitLayer);
        if (!string.IsNullOrWhiteSpace(normalizedExplicitLayer) && normalizedExplicitLayer != "auto")
        {
            return normalizedExplicitLayer == "all"
                ? ["working", "episodic", "semantic", "procedural", "self"]
                : [normalizedExplicitLayer];
        }

        if (LooksLikeIdentityFieldQuery(query))
        {
            // Identity lookups can span self-model keys and semantic claims.
            return ["self", "semantic"];
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
                    return normalized;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Memory routing model failed for retrieve_memory. Falling back to heuristics.");
        }

        return InferRetrieveLayersHeuristically(query);
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
                var semantic = await QuerySemanticCandidatesAsync(profile, cancellationToken);
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
                var proceduralTake = Math.Min(200, profile.Take * 6);
                var procedural = string.IsNullOrWhiteSpace(profile.RawQuery)
                    ? await proceduralMemoryRepository.QueryRecentAsync(proceduralTake, cancellationToken)
                    : await proceduralMemoryRepository.SearchAsync(profile.RawQuery, proceduralTake, cancellationToken);
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
                var snapshot = await selfModelRepository.GetAsync(cancellationToken);
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
        QueryProfile profile)
    {
        return candidates
            .Select(candidate => new RankedCandidate(candidate, ComputeCandidateScore(candidate, profile)))
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

    private static int ComputeCandidateScore(RetrievalCandidate candidate, QueryProfile profile)
    {
        var score = candidate.BaseScore;
        score += ScoreByStringSearch(candidate.Text, profile.Criteria);
        score += ComputeLayerPriorityBonus(candidate.Layer, profile.IsIdentityLike);
        score += ComputeRecencyBonus(candidate.OccurredAtUtc);
        score += ComputePayloadBonus(candidate, profile);
        return score;
    }

    private static int ComputePayloadBonus(RetrievalCandidate candidate, QueryProfile profile)
    {
        var bonus = 0;
        if (candidate.Payload is SemanticClaim claim)
        {
            bonus += claim.Status switch
            {
                SemanticClaimStatus.Active => 2,
                SemanticClaimStatus.Probabilistic => -1,
                SemanticClaimStatus.Superseded => -6,
                _ => 0
            };

            if (profile.IsIdentityLike
                && (ContainsAnyAlias(claim.Subject, profile.Criteria.Aliases)
                    || ContainsAnyAlias(claim.Predicate, profile.Criteria.Aliases)))
            {
                bonus += 3;
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

    private static int ComputeLayerPriorityBonus(string layer, bool isIdentityLike)
    {
        if (isIdentityLike)
        {
            return layer switch
            {
                "self" => 6,
                "semantic" => 4,
                "working" => 1,
                _ => 0
            };
        }

        return layer switch
        {
            "episodic" => 2,
            "semantic" => 2,
            "working" => 1,
            "procedural" => 1,
            "self" => 1,
            _ => 0
        };
    }

    private static int ComputeRecencyBonus(DateTimeOffset? occurredAtUtc)
    {
        if (!occurredAtUtc.HasValue)
        {
            return 0;
        }

        var ageHours = (DateTimeOffset.UtcNow - occurredAtUtc.Value).TotalHours;
        if (ageHours <= 24)
        {
            return 3;
        }

        if (ageHours <= 24 * 7)
        {
            return 2;
        }

        if (ageHours <= 24 * 30)
        {
            return 1;
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
        QueryProfile profile,
        CancellationToken cancellationToken)
    {
        PruneRequestCachesIfNeeded();
        var expandedTake = Math.Min(240, profile.Take * 8);
        if (string.IsNullOrWhiteSpace(profile.RawQuery))
        {
            return await semanticMemoryRepository.QueryClaimsAsync(
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

        var termTasks = terms
            .Select(term => QuerySemanticByTermCachedAsync(term, expandedTake, cancellationToken))
            .ToArray();
        var termResults = await Task.WhenAll(termTasks);
        var candidates = termResults
            .SelectMany(x => x)
            .ToList();

        if (candidates.Count < expandedTake)
        {
            var recent = await semanticMemoryRepository.QueryClaimsAsync(
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
        string term,
        int take,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"{term}|{take}";
        var wasCached = semanticTermCache.TryGetValue(cacheKey, out var cached);
        if (wasCached && cached is not null)
        {
            return await AwaitCachedSemanticTermAsync(cacheKey, cached);
        }

        var created = new Lazy<Task<IReadOnlyList<SemanticClaim>>>(
            () => QuerySemanticByTermAsync(term, take, cancellationToken),
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
        string term,
        int take,
        CancellationToken cancellationToken)
    {
        return await semanticMemoryRepository.SearchClaimsAsync(
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
            .Replace('-', '_');

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

    private sealed class RetrieveRoute
    {
        public string[]? Layers { get; set; }
    }

    private sealed record SearchCriteria(string NormalizedQuery, string[] Aliases, string[] Terms);
}
