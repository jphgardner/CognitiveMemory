using CognitiveMemory.Application.Abstractions;
using CognitiveMemory.Application.Cognitive;
using CognitiveMemory.Application.Consolidation;
using CognitiveMemory.Domain.Memory;
using Xunit;

namespace CognitiveMemory.Application.Tests;

public sealed class ConsolidationServiceTests
{
    [Fact]
    public async Task RunOnceAsync_Promotes_WhenExtractionIsValid()
    {
        var eventId = Guid.NewGuid();
        var episodic = new EpisodicMemoryEvent(
            eventId,
            "s1",
            "user",
            "Alice lives in Paris.",
            DateTimeOffset.UtcNow,
            "ctx",
            "test");

        var episodicRepo = new InMemoryEpisodicRepo([episodic]);
        var semanticRepo = new InMemorySemanticRepo();
        var stateRepo = new InMemoryStateRepo();
        var extractor = new StubExtractor(new ExtractedClaimCandidate("Alice", "lives in", "Paris", 0.9));
        var options = new ConsolidationOptions
        {
            MinExtractionConfidence = 0.5,
            MinOccurrencesForPromotion = 1
        };
        var directory = new InMemoryCompanionDirectory([new CompanionScope(Guid.NewGuid(), "s1", "u1")]);
        var profiles = new InMemoryCognitiveProfileResolver();

        var service = new ConsolidationService(episodicRepo, semanticRepo, stateRepo, extractor, directory, profiles, options);
        var result = await service.RunOnceAsync();

        Assert.Equal(1, result.Promoted);
        Assert.Single(semanticRepo.Claims);
        Assert.True(await stateRepo.IsProcessedAsync(eventId));
    }

    [Fact]
    public async Task RunOnceAsync_Skips_WhenBelowConfidenceThreshold()
    {
        var episodic = new EpisodicMemoryEvent(
            Guid.NewGuid(),
            "s1",
            "user",
            "Alice lives in Paris.",
            DateTimeOffset.UtcNow,
            "ctx",
            "test");

        var service = new ConsolidationService(
            new InMemoryEpisodicRepo([episodic]),
            new InMemorySemanticRepo(),
            new InMemoryStateRepo(),
            new StubExtractor(new ExtractedClaimCandidate("Alice", "lives in", "Paris", 0.3)),
            new InMemoryCompanionDirectory([new CompanionScope(Guid.NewGuid(), "s1", "u1")]),
            new InMemoryCognitiveProfileResolver(),
            new ConsolidationOptions
            {
                MinExtractionConfidence = 0.8,
                MinOccurrencesForPromotion = 1
            });

        var result = await service.RunOnceAsync();

        Assert.Equal(0, result.Promoted);
        Assert.Equal(1, result.Skipped);
    }

    private sealed class StubExtractor(ExtractedClaimCandidate? value) : IClaimExtractionGateway
    {
        public Task<ExtractedClaimCandidate?> ExtractAsync(string text, CancellationToken cancellationToken = default) => Task.FromResult(value);
    }

    private sealed class InMemoryEpisodicRepo(IReadOnlyList<EpisodicMemoryEvent> events) : IEpisodicMemoryRepository
    {
        public Task AppendAsync(EpisodicMemoryEvent memoryEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<EpisodicMemoryEvent>> QueryBySessionAsync(string sessionId, DateTimeOffset? fromUtc = null, DateTimeOffset? toUtc = null, int take = 100, CancellationToken cancellationToken = default)
            => Task.FromResult(events);

        public Task<IReadOnlyList<EpisodicMemoryEvent>> SearchBySessionAsync(string sessionId, string query, int take = 100, CancellationToken cancellationToken = default)
            => Task.FromResult(events);

        public Task<IReadOnlyList<EpisodicMemoryEvent>> QueryRangeAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, int take = 500, CancellationToken cancellationToken = default)
            => Task.FromResult(events);

        public Task<IReadOnlyList<EpisodicMemoryEvent>> QueryRangeAsync(Guid companionId, DateTimeOffset fromUtc, DateTimeOffset toUtc, int take = 500, CancellationToken cancellationToken = default)
            => Task.FromResult(events);
    }

    private sealed class InMemorySemanticRepo : ISemanticMemoryRepository
    {
        public List<SemanticClaim> Claims { get; } = [];

        public Task<SemanticClaim> CreateClaimAsync(SemanticClaim claim, CancellationToken cancellationToken = default)
        {
            Claims.Add(claim);
            return Task.FromResult(claim);
        }

        public Task<SemanticClaim?> GetByIdAsync(Guid claimId, CancellationToken cancellationToken = default)
            => Task.FromResult(Claims.FirstOrDefault(x => x.ClaimId == claimId));

        public Task SupersedeAsync(Guid claimId, Guid supersededByClaimId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<ClaimEvidence> AddEvidenceAsync(ClaimEvidence evidence, CancellationToken cancellationToken = default) => Task.FromResult(evidence);

        public Task<ClaimContradiction> AddContradictionAsync(ClaimContradiction contradiction, CancellationToken cancellationToken = default) => Task.FromResult(contradiction);

        public Task<IReadOnlyList<SemanticClaim>> QueryClaimsAsync(string? subject = null, string? predicate = null, SemanticClaimStatus? status = null, int take = 100, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SemanticClaim>>(Claims);

        public Task<IReadOnlyList<SemanticClaim>> SearchClaimsAsync(string query, int take = 100, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SemanticClaim>>(Claims);

        public Task<int> DecayActiveClaimsAsync(DateTimeOffset staleBeforeUtc, double decayStep, double minConfidence, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }

    private sealed class InMemoryStateRepo : IConsolidationStateRepository
    {
        private readonly HashSet<Guid> _processed = [];

        public Task<bool> IsProcessedAsync(Guid episodicEventId, CancellationToken cancellationToken = default)
            => Task.FromResult(_processed.Contains(episodicEventId));

        public Task MarkProcessedAsync(Guid episodicEventId, string outcome, Guid? semanticClaimId = null, string? notes = null, CancellationToken cancellationToken = default)
        {
            _processed.Add(episodicEventId);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryCompanionDirectory(IReadOnlyList<CompanionScope> companions) : ICompanionDirectory
    {
        public Task<IReadOnlyList<CompanionScope>> ListActiveAsync(CancellationToken cancellationToken = default) => Task.FromResult(companions);

        public Task<CompanionScope?> GetByCompanionIdAsync(Guid companionId, CancellationToken cancellationToken = default)
            => Task.FromResult(companions.FirstOrDefault(x => x.CompanionId == companionId));
    }

    private sealed class InMemoryCognitiveProfileResolver : ICompanionCognitiveProfileResolver
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
}
