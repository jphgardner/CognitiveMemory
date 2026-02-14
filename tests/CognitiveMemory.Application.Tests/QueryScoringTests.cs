using CognitiveMemory.Application.AI;
using CognitiveMemory.Application.Contracts;
using CognitiveMemory.Application.Interfaces;
using CognitiveMemory.Application.Services;
using CognitiveMemory.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using System.Linq;

namespace CognitiveMemory.Application.Tests;

public sealed class QueryScoringTests
{
    [Fact]
    public async Task QueryRankingIsDeterministicForSameInputs()
    {
        var service = new MemoryService(
            new NoopDocumentRepository(),
            new NoopDocumentIngestionPipeline(),
            new StableEmbeddingProvider(),
            new QueryFixtureClaimRepository(),
            new NoopQueryCache(),
            new NoopDebateOrchestrator(),
            new NoopHealthProbe(),
            NullLogger<MemoryService>.Instance);

        var request = new QueryClaimsRequest
        {
            Text = "What transport did we choose?",
            TopK = 2,
            IncludeEvidence = true,
            IncludeContradictions = true
        };

        var first = await service.QueryClaimsAsync(request, "req-1", CancellationToken.None);
        var second = await service.QueryClaimsAsync(request, "req-2", CancellationToken.None);

        Assert.Equal(first.Claims.Select(c => c.ClaimId), second.Claims.Select(c => c.ClaimId));
        Assert.Equal(first.Claims.Select(c => c.Score), second.Claims.Select(c => c.Score));
    }

    private sealed class QueryFixtureClaimRepository : IClaimRepository
    {
        public Task<IReadOnlyList<QueryCandidate>> GetQueryCandidatesAsync(string? subjectFilter, CancellationToken cancellationToken, int maxCandidates = 0)
        {
            var claims = new List<QueryCandidate>
            {
                new()
                {
                    ClaimId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    Predicate = "selected_transport",
                    LiteralValue = "SignalR",
                    Confidence = 0.82,
                    Scope = "{\"project\":\"PokemonMMO\"}",
                    LastReinforcedAt = DateTimeOffset.UtcNow.AddDays(-1),
                    Evidence =
                    [
                        new QueryEvidenceItem { EvidenceId = Guid.NewGuid(), SourceType = "ChatTurn", SourceRef = "c1", Strength = 0.8 }
                    ],
                    Contradictions = []
                },
                new()
                {
                    ClaimId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    Predicate = "selected_transport",
                    LiteralValue = "WebSockets",
                    Confidence = 0.70,
                    Scope = "{\"project\":\"PokemonMMO\"}",
                    LastReinforcedAt = DateTimeOffset.UtcNow.AddDays(-7),
                    Evidence =
                    [
                        new QueryEvidenceItem { EvidenceId = Guid.NewGuid(), SourceType = "ChatTurn", SourceRef = "c2", Strength = 0.55 }
                    ],
                    Contradictions =
                    [
                        new QueryContradictionItem { ContradictionId = Guid.NewGuid(), Type = "Direct", Severity = "High", Status = "Open" }
                    ]
                }
            };

            return Task.FromResult<IReadOnlyList<QueryCandidate>>(claims);
        }

        public Task<IReadOnlyList<ClaimListItem>> GetRecentAsync(int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ClaimListItem>>([]);
        public Task<ClaimCreatedResponse> CreateAsync(CreateClaimRequest request, CancellationToken cancellationToken) => Task.FromResult(new ClaimCreatedResponse { ClaimId = Guid.NewGuid(), Status = ClaimStatus.Active });
        public Task<Claim?> GetByHashAsync(string hash, CancellationToken cancellationToken) => Task.FromResult<Claim?>(null);
        public Task<QueryCandidate?> GetQueryCandidateByIdAsync(Guid claimId, CancellationToken cancellationToken) => Task.FromResult<QueryCandidate?>(null);
        public Task<ClaimLifecycleResponse> SupersedeAsync(Guid claimId, Guid replacementClaimId, CancellationToken cancellationToken) => Task.FromResult(new ClaimLifecycleResponse { ClaimId = claimId, Status = ClaimStatus.Superseded, UpdatedAt = DateTimeOffset.UtcNow });
        public Task<ClaimLifecycleResponse> RetractAsync(Guid claimId, CancellationToken cancellationToken) => Task.FromResult(new ClaimLifecycleResponse { ClaimId = claimId, Status = ClaimStatus.Retracted, UpdatedAt = DateTimeOffset.UtcNow });
        public Task<Guid> CreateManualContradictionAsync(Guid claimId, string reason, CancellationToken cancellationToken) => Task.FromResult(Guid.NewGuid());
        public Task<ClaimConfidenceUpdateResult?> TryApplyRecommendedConfidenceAsync(Guid claimId, double recommendedConfidence, double minDeltaToApply, double maxStep, CancellationToken cancellationToken) =>
            Task.FromResult<ClaimConfidenceUpdateResult?>(new ClaimConfidenceUpdateResult
            {
                ClaimId = claimId,
                PreviousConfidence = recommendedConfidence,
                UpdatedConfidence = recommendedConfidence,
                Applied = false,
                Reason = "not_used_in_query_tests"
            });
    }

    private sealed class StableEmbeddingProvider : ITextEmbeddingProvider
    {
        public Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken)
        {
            var seed = (text.Length % 10) / 10f;
            return Task.FromResult(new ReadOnlyMemory<float>([0.7f + seed, 0.3f, 0.2f, 0.1f]));
        }
    }

    private sealed class NoopDocumentRepository : IDocumentRepository
    {
        public Task<SourceDocument?> GetByIdAsync(Guid documentId, CancellationToken cancellationToken) => Task.FromResult<SourceDocument?>(null);
        public Task<SourceDocument?> GetBySourceRefAsync(string sourceType, string sourceRef, CancellationToken cancellationToken) => Task.FromResult<SourceDocument?>(null);
        public Task<SourceDocument?> GetBySourceHashAsync(string sourceType, string sourceRef, string contentHash, CancellationToken cancellationToken) => Task.FromResult<SourceDocument?>(null);
        public Task<SourceDocument> CreateAsync(string sourceType, string sourceRef, string content, string metadata, string contentHash, CancellationToken cancellationToken) => Task.FromResult(new SourceDocument { DocumentId = Guid.NewGuid() });
    }

    private sealed class NoopDocumentIngestionPipeline : IDocumentIngestionPipeline
    {
        public Task<int> ProcessDocumentAsync(SourceDocument document, CancellationToken cancellationToken) => Task.FromResult(0);
    }

    private sealed class NoopHealthProbe : ISystemHealthProbe
    {
        public Task<MemoryHealthResponse> CheckAsync(CancellationToken cancellationToken) => Task.FromResult(new MemoryHealthResponse());
    }

    private sealed class NoopQueryCache : IQueryCache
    {
        public Task<QueryClaimsResponse?> GetAsync(string key, CancellationToken cancellationToken) => Task.FromResult<QueryClaimsResponse?>(null);
        public Task SetAsync(string key, QueryClaimsResponse value, TimeSpan ttl, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NoopDebateOrchestrator : IDebateOrchestrator
    {
        public Task<DebateResult> OrchestrateAsync(string question, QueryClaimsResponse memoryPacket, CancellationToken cancellationToken) =>
            Task.FromResult(new DebateResult());
    }
}
