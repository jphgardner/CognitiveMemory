using System.Text.Json;
using CognitiveMemory.Application.Abstractions;
using CognitiveMemory.Application.Cognitive;
using CognitiveMemory.Domain.Memory;
using CognitiveMemory.Application.Relationships;
using CognitiveMemory.Infrastructure.Persistence.Entities;
using CognitiveMemory.Infrastructure.Scheduling;
using CognitiveMemory.Infrastructure.SemanticKernel;
using CognitiveMemory.Infrastructure.SemanticKernel.Plugins;
using CognitiveMemory.Infrastructure.Companions;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Xunit;

namespace CognitiveMemory.Api.Tests;

public sealed class MemoryToolsPluginTests
{
    [Fact]
    public async Task RetrieveMemoryAsync_IdentityOriginQuery_FindsSemanticOriginClaim()
    {
        var semanticRepository = new InMemorySemanticMemoryRepository();
        var now = DateTimeOffset.UtcNow;
        await semanticRepository.CreateClaimAsync(
            new SemanticClaim(
                Guid.NewGuid(),
                "origin",
                "is",
                "Neo Tokyo",
                0.92,
                "global",
                SemanticClaimStatus.Active,
                null,
                null,
                null,
                now,
                now));

        var plugin = CreatePlugin(semanticMemoryRepository: semanticRepository);

        var payload = await plugin.RetrieveMemoryAsync(
            "session-1",
            "identity.origin",
            take: 10,
            layer: "semantic");

        using var json = JsonDocument.Parse(payload);
        var semanticClaims = json.RootElement.GetProperty("results").GetProperty("semantic");

        Assert.Equal(1, semanticClaims.GetArrayLength());
        Assert.Equal("origin", semanticClaims[0].GetProperty("subject").GetString());
        Assert.Equal("Neo Tokyo", semanticClaims[0].GetProperty("value").GetString());
    }

    [Fact]
    public async Task StoreMemoryAsync_OriginStatement_StoresIdentityOriginKey()
    {
        var selfModelRepository = new InMemorySelfModelRepository();
        var plugin = CreatePlugin(selfModelRepository: selfModelRepository);

        var payload = await plugin.StoreMemoryAsync("session-3", "You are from Neo Tokyo.");

        using var json = JsonDocument.Parse(payload);
        Assert.Equal("self", json.RootElement.GetProperty("layer").GetString());
        var entries = json.RootElement.GetProperty("entries");
        Assert.True(entries.GetArrayLength() >= 1);
        Assert.Equal("identity.origin", entries[0].GetProperty("key").GetString());

        var snapshot = await selfModelRepository.GetAsync();
        Assert.Contains(snapshot.Preferences, x => x.Key == "identity.origin");
    }

    [Fact]
    public async Task StoreMemoryAsync_UpdatingSelfIdentity_IncludesReplacementMetadata()
    {
        var selfModelRepository = new InMemorySelfModelRepository();
        var plugin = CreatePlugin(selfModelRepository: selfModelRepository);

        _ = await plugin.StoreMemoryAsync("session-33", "You are from Neo Tokyo.");
        var payload = await plugin.StoreMemoryAsync("session-33", "You are from New Babylon.");

        using var json = JsonDocument.Parse(payload);
        var entries = json.RootElement.GetProperty("entries");
        Assert.True(entries.GetArrayLength() >= 1);
        Assert.True(entries[0].GetProperty("replacedPrevious").GetBoolean());
        var previousValue = entries[0].GetProperty("previousValue").GetString();
        Assert.NotNull(previousValue);
        Assert.Contains("Neo Tokyo", previousValue!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("identity.origin", entries[0].GetProperty("key").GetString());
        var currentValue = entries[0].GetProperty("value").GetString();
        Assert.NotNull(currentValue);
        Assert.Contains("New Babylon", currentValue!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RetrieveMemoryAsync_AuditPersistenceFailure_StillReturnsAndLogs()
    {
        var logger = new ListLogger<MemoryToolsPlugin>();
        var plugin = CreatePlugin(
            toolInvocationAuditRepository: new ThrowingToolInvocationAuditRepository(),
            logger: logger);

        var payload = await plugin.RetrieveMemoryAsync(
            "session-2",
            "anything",
            take: 5,
            layer: "working");

        using var json = JsonDocument.Parse(payload);
        Assert.True(json.RootElement.GetProperty("results").TryGetProperty("working", out _));

        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Information
                     && entry.Message.Contains("Memory tool execution started", StringComparison.Ordinal));
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Information
                     && entry.Message.Contains("Memory tool execution succeeded", StringComparison.Ordinal));
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Warning
                     && entry.Message.Contains("Memory tool audit persistence failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StoreMemoryAsync_DuplicateSemanticClaim_IsDeduplicated()
    {
        var semanticRepository = new InMemorySemanticMemoryRepository();
        var plugin = CreatePlugin(semanticMemoryRepository: semanticRepository);

        _ = await plugin.StoreMemoryAsync("session-sem-1", "Deployment status is green.");
        var second = await plugin.StoreMemoryAsync("session-sem-1", "Deployment status is green.");

        using var json = JsonDocument.Parse(second);
        Assert.Equal("semantic", json.RootElement.GetProperty("layer").GetString());
        Assert.True(json.RootElement.GetProperty("deduplicated").GetBoolean());
        Assert.Equal(1, semanticRepository.Count);
    }

    [Fact]
    public async Task RetrieveMemoryAsync_IdentityQuery_PrioritizesSelfEvidenceAndAddsMetrics()
    {
        var selfModelRepository = new InMemorySelfModelRepository();
        await selfModelRepository.SetPreferenceAsync("identity.origin", "Neo Tokyo");

        var semanticRepository = new InMemorySemanticMemoryRepository();
        var now = DateTimeOffset.UtcNow;
        await semanticRepository.CreateClaimAsync(
            new SemanticClaim(
                Guid.NewGuid(),
                "origin",
                "is",
                "Old Earth",
                0.75,
                "global",
                SemanticClaimStatus.Active,
                null,
                null,
                null,
                now,
                now));

        var plugin = CreatePlugin(
            selfModelRepository: selfModelRepository,
            semanticMemoryRepository: semanticRepository);

        var payload = await plugin.RetrieveMemoryAsync("session-4", "identity.origin", take: 5);

        using var json = JsonDocument.Parse(payload);
        var selectedLayers = json.RootElement.GetProperty("selectedLayers");
        Assert.Equal(2, selectedLayers.GetArrayLength());
        Assert.Equal("self", selectedLayers[0].GetString());
        Assert.Equal("semantic", selectedLayers[1].GetString());

        var evidence = json.RootElement.GetProperty("evidence");
        Assert.True(evidence.GetArrayLength() > 0);
        Assert.Equal("self", evidence[0].GetProperty("layer").GetString());

        var insights = json.RootElement.GetProperty("insights");
        Assert.True(insights.GetProperty("hasConflicts").GetBoolean());
        var identityProfile = insights.GetProperty("identityProfile");
        Assert.Equal("Neo Tokyo", identityProfile.GetProperty("identity.origin").GetString());

        var layers = json.RootElement.GetProperty("layers");
        Assert.True(layers.GetArrayLength() >= 2);
        Assert.True(layers[0].TryGetProperty("elapsedMs", out _));
        Assert.True(layers[0].TryGetProperty("fromCache", out _));

        var metrics = json.RootElement.GetProperty("metrics");
        Assert.True(metrics.GetProperty("candidateCount").GetInt32() >= 1);
        Assert.True(metrics.GetProperty("rankedCount").GetInt32() >= 1);
    }

    private static MemoryToolsPlugin CreatePlugin(
        IWorkingMemoryStore? workingMemoryStore = null,
        IEpisodicMemoryRepository? episodicMemoryRepository = null,
        ISemanticMemoryRepository? semanticMemoryRepository = null,
        IProceduralMemoryRepository? proceduralMemoryRepository = null,
        ISelfModelRepository? selfModelRepository = null,
        IMemoryRelationshipRepository? memoryRelationshipRepository = null,
        IMemoryRelationshipExtractionService? memoryRelationshipExtractionService = null,
        IToolInvocationAuditRepository? toolInvocationAuditRepository = null,
        IScheduledActionStore? scheduledActionStore = null,
        ILogger<MemoryToolsPlugin>? logger = null)
    {
        return new MemoryToolsPlugin(
            workingMemoryStore ?? new InMemoryWorkingMemoryStore(),
            episodicMemoryRepository ?? new InMemoryEpisodicMemoryRepository(),
            semanticMemoryRepository ?? new InMemorySemanticMemoryRepository(),
            proceduralMemoryRepository ?? new InMemoryProceduralMemoryRepository(),
            selfModelRepository ?? new InMemorySelfModelRepository(),
            memoryRelationshipRepository ?? new InMemoryMemoryRelationshipRepository(),
            memoryRelationshipExtractionService ?? new InMemoryMemoryRelationshipExtractionService(),
            toolInvocationAuditRepository ?? new InMemoryToolInvocationAuditRepository(),
            scheduledActionStore ?? new InMemoryScheduledActionStore(),
            new InMemoryCompanionScopeResolver(),
            new InMemoryCompanionCognitiveProfileResolver(),
            new ClaimExtractionKernel(Kernel.CreateBuilder().Build()),
            new SemanticKernelOptions { Provider = "InMemory" },
            logger ?? new ListLogger<MemoryToolsPlugin>());
    }

    private sealed class InMemoryWorkingMemoryStore : IWorkingMemoryStore
    {
        private readonly Dictionary<string, WorkingMemoryContext> contexts = new(StringComparer.Ordinal);

        public Task<WorkingMemoryContext> GetAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            if (contexts.TryGetValue(sessionId, out var existing))
            {
                return Task.FromResult(existing);
            }

            return Task.FromResult(new WorkingMemoryContext(sessionId, []));
        }

        public Task SaveAsync(WorkingMemoryContext context, CancellationToken cancellationToken = default)
        {
            contexts[context.SessionId] = context;
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryEpisodicMemoryRepository : IEpisodicMemoryRepository
    {
        private readonly List<EpisodicMemoryEvent> items = [];

        public Task AppendAsync(EpisodicMemoryEvent memoryEvent, CancellationToken cancellationToken = default)
        {
            items.Add(memoryEvent);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<EpisodicMemoryEvent>> QueryBySessionAsync(
            string sessionId,
            DateTimeOffset? fromUtc = null,
            DateTimeOffset? toUtc = null,
            int take = 100,
            CancellationToken cancellationToken = default)
        {
            var query = items
                .Where(x => x.SessionId == sessionId)
                .OrderByDescending(x => x.OccurredAt)
                .Take(Math.Clamp(take, 1, 500))
                .ToArray();

            return Task.FromResult<IReadOnlyList<EpisodicMemoryEvent>>(query);
        }

        public Task<IReadOnlyList<EpisodicMemoryEvent>> SearchBySessionAsync(
            string sessionId,
            string query,
            int take = 100,
            CancellationToken cancellationToken = default)
        {
            var normalized = query.Trim().ToLowerInvariant();
            var rows = items
                .Where(x => x.SessionId == sessionId)
                .Where(
                    x => x.Who.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                         || x.What.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                         || x.Context.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                         || x.SourceReference.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.OccurredAt)
                .Take(Math.Clamp(take, 1, 500))
                .ToArray();

            return Task.FromResult<IReadOnlyList<EpisodicMemoryEvent>>(rows);
        }

        public Task<IReadOnlyList<EpisodicMemoryEvent>> QueryRangeAsync(
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            int take = 500,
            CancellationToken cancellationToken = default)
        {
            var query = items
                .Where(x => x.OccurredAt >= fromUtc && x.OccurredAt <= toUtc)
                .OrderByDescending(x => x.OccurredAt)
                .Take(Math.Clamp(take, 1, 1000))
                .ToArray();

            return Task.FromResult<IReadOnlyList<EpisodicMemoryEvent>>(query);
        }

        public Task<IReadOnlyList<EpisodicMemoryEvent>> QueryRangeAsync(
            Guid companionId,
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            int take = 500,
            CancellationToken cancellationToken = default)
            => QueryRangeAsync(fromUtc, toUtc, take, cancellationToken);
    }

    private sealed class InMemorySemanticMemoryRepository : ISemanticMemoryRepository
    {
        private readonly List<SemanticClaim> claims = [];
        public int Count => claims.Count;

        public Task<SemanticClaim> CreateClaimAsync(SemanticClaim claim, CancellationToken cancellationToken = default)
        {
            claims.Add(claim);
            return Task.FromResult(claim);
        }

        public Task<SemanticClaim?> GetByIdAsync(Guid claimId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(claims.FirstOrDefault(x => x.ClaimId == claimId));
        }

        public Task SupersedeAsync(Guid claimId, Guid supersededByClaimId, CancellationToken cancellationToken = default)
        {
            var index = claims.FindIndex(x => x.ClaimId == claimId);
            if (index < 0)
            {
                return Task.CompletedTask;
            }

            var existing = claims[index];
            claims[index] = existing with
            {
                Status = SemanticClaimStatus.Superseded,
                SupersededByClaimId = supersededByClaimId,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            return Task.CompletedTask;
        }

        public Task<ClaimEvidence> AddEvidenceAsync(ClaimEvidence evidence, CancellationToken cancellationToken = default)
            => Task.FromResult(evidence);

        public Task<ClaimContradiction> AddContradictionAsync(ClaimContradiction contradiction, CancellationToken cancellationToken = default)
            => Task.FromResult(contradiction);

        public Task<int> DecayActiveClaimsAsync(
            DateTimeOffset staleBeforeUtc,
            double decayStep,
            double minConfidence,
            CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        public Task<IReadOnlyList<SemanticClaim>> QueryClaimsAsync(
            string? subject = null,
            string? predicate = null,
            SemanticClaimStatus? status = null,
            int take = 100,
            CancellationToken cancellationToken = default)
        {
            IEnumerable<SemanticClaim> query = claims;

            if (!string.IsNullOrWhiteSpace(subject))
            {
                var normalized = subject.Trim().ToLowerInvariant();
                query = query.Where(x => x.Subject.ToLowerInvariant().Contains(normalized, StringComparison.Ordinal));
            }

            if (!string.IsNullOrWhiteSpace(predicate))
            {
                var normalized = predicate.Trim().ToLowerInvariant();
                query = query.Where(x => x.Predicate.ToLowerInvariant().Contains(normalized, StringComparison.Ordinal));
            }

            if (status is not null)
            {
                query = query.Where(x => x.Status == status.Value);
            }

            return Task.FromResult<IReadOnlyList<SemanticClaim>>(
                query.Take(Math.Clamp(take, 1, 500)).ToArray());
        }

        public Task<IReadOnlyList<SemanticClaim>> SearchClaimsAsync(
            string query,
            int take = 100,
            CancellationToken cancellationToken = default)
        {
            var normalized = query.Trim().ToLowerInvariant();
            var rows = claims
                .Where(
                    x => x.Subject.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                         || x.Predicate.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                         || x.Value.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                         || x.Scope.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                .Take(Math.Clamp(take, 1, 500))
                .ToArray();

            return Task.FromResult<IReadOnlyList<SemanticClaim>>(rows);
        }
    }

    private sealed class InMemoryProceduralMemoryRepository : IProceduralMemoryRepository
    {
        private readonly List<ProceduralRoutine> routines = [];

        public Task<ProceduralRoutine> UpsertAsync(ProceduralRoutine routine, CancellationToken cancellationToken = default)
        {
            routines.Add(routine);
            return Task.FromResult(routine);
        }

        public Task<IReadOnlyList<ProceduralRoutine>> QueryByTriggerAsync(string trigger, int take = 20, CancellationToken cancellationToken = default)
        {
            var query = routines
                .Where(x => x.Trigger.Contains(trigger, StringComparison.OrdinalIgnoreCase))
                .Take(Math.Clamp(take, 1, 100))
                .ToArray();

            return Task.FromResult<IReadOnlyList<ProceduralRoutine>>(query);
        }

        public Task<IReadOnlyList<ProceduralRoutine>> QueryRecentAsync(int take = 50, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<ProceduralRoutine>>(
                routines.Take(Math.Clamp(take, 1, 100)).ToArray());
        }

        public Task<IReadOnlyList<ProceduralRoutine>> SearchAsync(string query, int take = 20, CancellationToken cancellationToken = default)
        {
            var results = routines
                .Where(x =>
                    x.Trigger.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || x.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || x.Outcome.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Take(Math.Clamp(take, 1, 100))
                .ToArray();

            return Task.FromResult<IReadOnlyList<ProceduralRoutine>>(results);
        }
    }

    private sealed class InMemorySelfModelRepository : ISelfModelRepository
    {
        private readonly Dictionary<string, SelfPreference> items = new(StringComparer.Ordinal);

        public Task<SelfModelSnapshot> GetAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new SelfModelSnapshot(items.Values.ToArray()));
        }

        public Task SetPreferenceAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            items[key] = new SelfPreference(key, value, DateTimeOffset.UtcNow);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryToolInvocationAuditRepository : IToolInvocationAuditRepository
    {
        private readonly List<ToolInvocationAudit> items = [];

        public Task AddAsync(ToolInvocationAudit audit, CancellationToken cancellationToken = default)
        {
            items.Add(audit);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ToolInvocationAudit>> QueryByExecutedAtRangeAsync(
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            int take = 50,
            CancellationToken cancellationToken = default)
        {
            var query = items
                .Where(x => x.ExecutedAtUtc >= fromUtc && x.ExecutedAtUtc <= toUtc)
                .OrderByDescending(x => x.ExecutedAtUtc)
                .Take(Math.Clamp(take, 1, 500))
                .ToArray();

            return Task.FromResult<IReadOnlyList<ToolInvocationAudit>>(query);
        }
    }

    private sealed class InMemoryMemoryRelationshipRepository : IMemoryRelationshipRepository
    {
        private readonly List<MemoryRelationship> rows = [];

        public Task<MemoryRelationship> UpsertAsync(MemoryRelationship relationship, CancellationToken cancellationToken = default)
        {
            var existing = rows.FirstOrDefault(
                x => x.SessionId == relationship.SessionId
                     && x.FromType == relationship.FromType
                     && x.FromId == relationship.FromId
                     && x.ToType == relationship.ToType
                     && x.ToId == relationship.ToId
                     && x.RelationshipType == relationship.RelationshipType);
            if (existing is null)
            {
                rows.Add(relationship);
                return Task.FromResult(relationship);
            }

            var updated = existing with
            {
                Confidence = Math.Max(existing.Confidence, relationship.Confidence),
                Strength = Math.Max(existing.Strength, relationship.Strength),
                Status = MemoryRelationshipStatus.Active,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            var idx = rows.FindIndex(x => x.RelationshipId == existing.RelationshipId);
            rows[idx] = updated;
            return Task.FromResult(updated);
        }

        public Task<bool> RetireAsync(Guid relationshipId, CancellationToken cancellationToken = default)
        {
            var idx = rows.FindIndex(x => x.RelationshipId == relationshipId);
            if (idx < 0)
            {
                return Task.FromResult(false);
            }

            rows[idx] = rows[idx] with { Status = MemoryRelationshipStatus.Retired, UpdatedAtUtc = DateTimeOffset.UtcNow };
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<MemoryRelationship>> QueryBySessionAsync(
            string sessionId,
            string? relationshipType = null,
            MemoryRelationshipStatus? status = null,
            int take = 200,
            CancellationToken cancellationToken = default)
        {
            IEnumerable<MemoryRelationship> query = rows.Where(x => x.SessionId == sessionId);
            if (!string.IsNullOrWhiteSpace(relationshipType))
            {
                query = query.Where(x => x.RelationshipType == relationshipType);
            }

            if (status.HasValue)
            {
                query = query.Where(x => x.Status == status.Value);
            }

            return Task.FromResult<IReadOnlyList<MemoryRelationship>>(query.Take(Math.Clamp(take, 1, 1000)).ToArray());
        }

        public Task<IReadOnlyList<MemoryRelationship>> QueryByNodeAsync(
            string sessionId,
            MemoryNodeType nodeType,
            string nodeId,
            string? relationshipType = null,
            int take = 200,
            CancellationToken cancellationToken = default)
        {
            IEnumerable<MemoryRelationship> query = rows.Where(
                x => x.SessionId == sessionId
                     && ((x.FromType == nodeType && x.FromId == nodeId)
                         || (x.ToType == nodeType && x.ToId == nodeId)));
            if (!string.IsNullOrWhiteSpace(relationshipType))
            {
                query = query.Where(x => x.RelationshipType == relationshipType);
            }

            return Task.FromResult<IReadOnlyList<MemoryRelationship>>(query.Take(Math.Clamp(take, 1, 1000)).ToArray());
        }

        public Task<IReadOnlyDictionary<Guid, int>> GetSemanticRelationshipDegreeAsync(
            string sessionId,
            IReadOnlyList<Guid> semanticClaimIds,
            CancellationToken cancellationToken = default)
        {
            var set = semanticClaimIds.Select(x => x.ToString("N")).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var output = new Dictionary<Guid, int>();
            foreach (var row in rows.Where(x => x.SessionId == sessionId && x.Status == MemoryRelationshipStatus.Active))
            {
                if (row.FromType == MemoryNodeType.SemanticClaim
                    && set.Contains(row.FromId)
                    && Guid.TryParseExact(row.FromId, "N", out var fromId))
                {
                    output[fromId] = (output.TryGetValue(fromId, out var c) ? c : 0) + 1;
                }

                if (row.ToType == MemoryNodeType.SemanticClaim
                    && set.Contains(row.ToId)
                    && Guid.TryParseExact(row.ToId, "N", out var toId))
                {
                    output[toId] = (output.TryGetValue(toId, out var c) ? c : 0) + 1;
                }
            }

            return Task.FromResult<IReadOnlyDictionary<Guid, int>>(output);
        }

        public Task<MemoryRelationshipBackfillResult> BackfillAsync(string? sessionId = null, int take = 2000, CancellationToken cancellationToken = default)
            => Task.FromResult(new MemoryRelationshipBackfillResult(0, 0, 0));
    }

    private sealed class InMemoryMemoryRelationshipExtractionService : IMemoryRelationshipExtractionService
    {
        public Task<MemoryRelationshipExtractionResult> ExtractAsync(
            string sessionId,
            int take = 200,
            bool apply = true,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new MemoryRelationshipExtractionResult(sessionId, 0, 0, 0, 0, apply, "test"));
    }

    private sealed class InMemoryScheduledActionStore : IScheduledActionStore
    {
        private readonly Dictionary<Guid, ScheduledActionEntity> rows = [];

        public Task<ScheduledActionEntity> ScheduleAsync(
            string sessionId,
            string actionType,
            string inputJson,
            DateTimeOffset runAtUtc,
            int maxAttempts,
            CancellationToken cancellationToken = default)
        {
            var now = DateTimeOffset.UtcNow;
            var row = new ScheduledActionEntity
            {
                ActionId = Guid.NewGuid(),
                SessionId = sessionId,
                ActionType = actionType,
                InputJson = inputJson,
                RunAtUtc = runAtUtc,
                Status = ScheduledActionStatus.Pending,
                Attempts = 0,
                MaxAttempts = maxAttempts,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            rows[row.ActionId] = row;
            return Task.FromResult(row);
        }

        public Task<IReadOnlyList<ScheduledActionEntity>> ListAsync(string? sessionId, string? status, int take, CancellationToken cancellationToken = default)
        {
            IEnumerable<ScheduledActionEntity> query = rows.Values;
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                query = query.Where(x => x.SessionId == sessionId);
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(x => x.Status == status);
            }

            return Task.FromResult<IReadOnlyList<ScheduledActionEntity>>(query.Take(Math.Clamp(take, 1, 200)).ToArray());
        }

        public Task<bool> CancelAsync(Guid actionId, CancellationToken cancellationToken = default)
        {
            if (!rows.TryGetValue(actionId, out var row))
            {
                return Task.FromResult(false);
            }

            row.Status = ScheduledActionStatus.Canceled;
            row.UpdatedAtUtc = DateTimeOffset.UtcNow;
            row.CompletedAtUtc = row.UpdatedAtUtc;
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<ScheduledActionEntity>> ClaimDueAsync(DateTimeOffset nowUtc, int batchSize, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ScheduledActionEntity>>([]);

        public Task MarkCompletedAsync(Guid actionId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task MarkFailedAsync(Guid actionId, string error, bool exhaustedRetries, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class InMemoryCompanionScopeResolver : ICompanionScopeResolver
    {
        public Task<Guid?> TryResolveCompanionIdAsync(string sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult<Guid?>(GuidUtility.FromString(sessionId));

        public Task<Guid> ResolveCompanionIdOrThrowAsync(string sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult(GuidUtility.FromString(sessionId));
    }

    private sealed class InMemoryCompanionCognitiveProfileResolver : ICompanionCognitiveProfileResolver
    {
        public Task<ResolvedCompanionCognitiveProfile> ResolveByCompanionIdAsync(Guid companionId, CancellationToken cancellationToken = default)
        {
            var profile = new CompanionCognitiveProfileDocument();
            return Task.FromResult(
                new ResolvedCompanionCognitiveProfile(
                    companionId,
                    Guid.Empty,
                    1,
                    profile,
                    new CompanionCognitiveRuntimePolicy(companionId, Guid.Empty, 1, profile, new RuntimeLimits(120, 20, 8, 1)),
                    IsFallback: true));
        }

        public Task<ResolvedCompanionCognitiveProfile> ResolveBySessionIdAsync(string sessionId, CancellationToken cancellationToken = default)
            => ResolveByCompanionIdAsync(Guid.Empty, cancellationToken);
    }

    private static class GuidUtility
    {
        public static Guid FromString(string value)
        {
            var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
            var bytes = new byte[16];
            Array.Copy(hash, bytes, 16);
            return new Guid(bytes);
        }
    }

    private sealed class ThrowingToolInvocationAuditRepository : IToolInvocationAuditRepository
    {
        public Task AddAsync(ToolInvocationAudit audit, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Simulated audit persistence outage.");

        public Task<IReadOnlyList<ToolInvocationAudit>> QueryByExecutedAtRangeAsync(
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            int take = 50,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ToolInvocationAudit>>([]);
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }

        public sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
