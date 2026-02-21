using CognitiveMemory.Application.Abstractions;
using CognitiveMemory.Application.Planning;
using CognitiveMemory.Application.Reasoning;
using CognitiveMemory.Application.Truth;
using CognitiveMemory.Domain.Memory;
using Xunit;

namespace CognitiveMemory.Application.Tests;

public sealed class CognitiveEvolutionServiceTests
{
    [Fact]
    public async Task ReasoningRunOnce_InfersClaim_AndSuggestsProcedure()
    {
        var now = DateTimeOffset.UtcNow;
        var episodes = new[]
        {
            new EpisodicMemoryEvent(Guid.NewGuid(), "s1", "user", "Alice prefers dark mode.", now.AddMinutes(-4), "ctx", "test:1"),
            new EpisodicMemoryEvent(Guid.NewGuid(), "s1", "user", "Alice prefers dark mode.", now.AddMinutes(-2), "ctx", "test:2")
        };

        var episodicRepo = new InMemoryEpisodicRepo(episodes);
        var semanticRepo = new InMemorySemanticRepo();
        var proceduralRepo = new InMemoryProceduralRepo();
        var options = new CognitiveReasoningOptions
        {
            LookbackHours = 24,
            MinPatternOccurrences = 2,
            SuggestProceduralPatterns = true
        };

        var service = new CognitiveReasoningService(episodicRepo, semanticRepo, proceduralRepo, options);
        var result = await service.RunOnceAsync();

        Assert.Equal(1, result.InferredClaims);
        Assert.Equal(1, result.ProceduralSuggestions);
        Assert.Equal(1, semanticRepo.Claims.Count(x => x.Status == SemanticClaimStatus.Active));
        Assert.Single(proceduralRepo.Routines);
    }

    [Fact]
    public async Task TruthMaintenanceRunOnce_RecordsConflict_AndMarksProbabilistic()
    {
        var now = DateTimeOffset.UtcNow;
        var claimA = new SemanticClaim(Guid.NewGuid(), "deployment", "status", "green", 0.8, "global", SemanticClaimStatus.Active, null, null, null, now, now);
        var claimB = new SemanticClaim(Guid.NewGuid(), "deployment", "status", "red", 0.75, "global", SemanticClaimStatus.Active, null, null, null, now, now);

        var semanticRepo = new InMemorySemanticRepo([claimA, claimB]);
        var service = new TruthMaintenanceService(
            semanticRepo,
            new TruthMaintenanceOptions
            {
                UncertainThreshold = 0.9,
                ConflictConfidencePenalty = 0.1,
                MaxConflictPairsPerRun = 10
            });

        var result = await service.RunOnceAsync();

        Assert.Equal(1, result.ConflictClusters);
        Assert.Equal(1, result.ContradictionsRecorded);
        Assert.True(result.ConfidenceAdjustments >= 2);
        Assert.True(result.ProbabilisticMarks >= 2);
    }

    [Fact]
    public async Task GoalPlanning_RecordOutcome_UpsertsRoutineOnSuccess()
    {
        var now = DateTimeOffset.UtcNow;
        var episodicRepo = new InMemoryEpisodicRepo(
        [
            new EpisodicMemoryEvent(Guid.NewGuid(), "s1", "user", "Need deployment checklist for API release", now.AddHours(-6), "ctx", "ep:1")
        ]);
        var proceduralRepo = new InMemoryProceduralRepo();
        var service = new GoalPlanningService(episodicRepo, proceduralRepo, new GoalPlanningOptions());

        var plan = await service.GeneratePlanAsync(new GenerateGoalPlanRequest("s1", "Prepare API deployment checklist"));
        var outcome = await service.RecordOutcomeAsync(
            new RecordGoalOutcomeRequest(
                plan.PlanId,
                "s1",
                "Prepare API deployment checklist",
                true,
                ["Collect environment configs", "Run smoke tests", "Deploy and verify"],
                "Deployment checklist succeeded."));

        Assert.True(outcome.ProceduralMemoryUpdated);
        Assert.NotNull(outcome.RoutineId);
        Assert.Single(proceduralRepo.Routines);
    }

    private sealed class InMemoryEpisodicRepo(IReadOnlyList<EpisodicMemoryEvent> events) : IEpisodicMemoryRepository
    {
        private readonly List<EpisodicMemoryEvent> _events = [.. events];

        public Task AppendAsync(EpisodicMemoryEvent memoryEvent, CancellationToken cancellationToken = default)
        {
            _events.Add(memoryEvent);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<EpisodicMemoryEvent>> QueryBySessionAsync(string sessionId, DateTimeOffset? fromUtc = null, DateTimeOffset? toUtc = null, int take = 100, CancellationToken cancellationToken = default)
        {
            var rows = _events
                .Where(x => x.SessionId == sessionId)
                .OrderByDescending(x => x.OccurredAt)
                .Take(take)
                .ToArray();

            return Task.FromResult<IReadOnlyList<EpisodicMemoryEvent>>(rows);
        }

        public Task<IReadOnlyList<EpisodicMemoryEvent>> SearchBySessionAsync(string sessionId, string query, int take = 100, CancellationToken cancellationToken = default)
        {
            var normalized = query.Trim().ToLowerInvariant();
            var rows = _events
                .Where(x => x.SessionId == sessionId)
                .Where(
                    x => x.Who.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                         || x.What.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                         || x.Context.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                         || x.SourceReference.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.OccurredAt)
                .Take(take)
                .ToArray();

            return Task.FromResult<IReadOnlyList<EpisodicMemoryEvent>>(rows);
        }

        public Task<IReadOnlyList<EpisodicMemoryEvent>> QueryRangeAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, int take = 500, CancellationToken cancellationToken = default)
        {
            var rows = _events
                .Where(x => x.OccurredAt >= fromUtc && x.OccurredAt <= toUtc)
                .OrderByDescending(x => x.OccurredAt)
                .Take(take)
                .ToArray();

            return Task.FromResult<IReadOnlyList<EpisodicMemoryEvent>>(rows);
        }
    }

    private sealed class InMemorySemanticRepo : ISemanticMemoryRepository
    {
        public List<SemanticClaim> Claims { get; } = [];
        public List<ClaimContradiction> Contradictions { get; } = [];
        public List<ClaimEvidence> Evidence { get; } = [];

        public InMemorySemanticRepo()
        {
        }

        public InMemorySemanticRepo(IEnumerable<SemanticClaim> claims)
        {
            Claims.AddRange(claims);
        }

        public Task<SemanticClaim> CreateClaimAsync(SemanticClaim claim, CancellationToken cancellationToken = default)
        {
            Claims.Add(claim);
            return Task.FromResult(claim);
        }

        public Task<SemanticClaim?> GetByIdAsync(Guid claimId, CancellationToken cancellationToken = default)
            => Task.FromResult(Claims.FirstOrDefault(x => x.ClaimId == claimId));

        public Task SupersedeAsync(Guid claimId, Guid supersededByClaimId, CancellationToken cancellationToken = default)
        {
            var existing = Claims.FirstOrDefault(x => x.ClaimId == claimId);
            if (existing is not null)
            {
                Claims.Remove(existing);
                Claims.Add(existing with
                {
                    Status = SemanticClaimStatus.Superseded,
                    SupersededByClaimId = supersededByClaimId,
                    ValidToUtc = DateTimeOffset.UtcNow,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                });
            }

            return Task.CompletedTask;
        }

        public Task<ClaimEvidence> AddEvidenceAsync(ClaimEvidence evidence, CancellationToken cancellationToken = default)
        {
            Evidence.Add(evidence);
            return Task.FromResult(evidence);
        }

        public Task<ClaimContradiction> AddContradictionAsync(ClaimContradiction contradiction, CancellationToken cancellationToken = default)
        {
            var exists = Contradictions.Any(
                x => (x.ClaimAId == contradiction.ClaimAId && x.ClaimBId == contradiction.ClaimBId)
                     || (x.ClaimAId == contradiction.ClaimBId && x.ClaimBId == contradiction.ClaimAId));

            if (!exists)
            {
                Contradictions.Add(contradiction);
            }

            return Task.FromResult(contradiction);
        }

        public Task<int> DecayActiveClaimsAsync(DateTimeOffset staleBeforeUtc, double decayStep, double minConfidence, CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        public Task<IReadOnlyList<SemanticClaim>> QueryClaimsAsync(string? subject = null, string? predicate = null, SemanticClaimStatus? status = null, int take = 100, CancellationToken cancellationToken = default)
        {
            var query = Claims.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(subject))
            {
                query = query.Where(x => x.Subject == subject);
            }

            if (!string.IsNullOrWhiteSpace(predicate))
            {
                query = query.Where(x => x.Predicate == predicate);
            }

            if (status is not null)
            {
                query = query.Where(x => x.Status == status.Value);
            }

            return Task.FromResult<IReadOnlyList<SemanticClaim>>(query.Take(take).ToArray());
        }

        public Task<IReadOnlyList<SemanticClaim>> SearchClaimsAsync(string query, int take = 100, CancellationToken cancellationToken = default)
        {
            var normalized = query.Trim().ToLowerInvariant();
            var rows = Claims
                .Where(
                    x => x.Subject.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                         || x.Predicate.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                         || x.Value.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                         || x.Scope.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                .Take(take)
                .ToArray();

            return Task.FromResult<IReadOnlyList<SemanticClaim>>(rows);
        }
    }

    private sealed class InMemoryProceduralRepo : IProceduralMemoryRepository
    {
        public List<ProceduralRoutine> Routines { get; } = [];

        public Task<ProceduralRoutine> UpsertAsync(ProceduralRoutine routine, CancellationToken cancellationToken = default)
        {
            var index = Routines.FindIndex(x => x.RoutineId == routine.RoutineId);
            if (index >= 0)
            {
                Routines[index] = routine;
            }
            else
            {
                Routines.Add(routine);
            }

            return Task.FromResult(routine);
        }

        public Task<IReadOnlyList<ProceduralRoutine>> QueryByTriggerAsync(string trigger, int take = 20, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ProceduralRoutine>>(
                Routines.Where(x => x.Trigger == trigger).Take(take).ToArray());

        public Task<IReadOnlyList<ProceduralRoutine>> QueryRecentAsync(int take = 50, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ProceduralRoutine>>(
                Routines.OrderByDescending(x => x.UpdatedAtUtc).Take(take).ToArray());

        public Task<IReadOnlyList<ProceduralRoutine>> SearchAsync(string query, int take = 20, CancellationToken cancellationToken = default)
        {
            var normalized = query.ToLowerInvariant();
            return Task.FromResult<IReadOnlyList<ProceduralRoutine>>(
                Routines.Where(x => x.Trigger.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                                    || x.Name.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                                    || x.Outcome.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                    .Take(take)
                    .ToArray());
        }
    }
}
