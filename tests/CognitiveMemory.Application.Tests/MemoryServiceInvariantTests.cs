using CognitiveMemory.Application.AI;
using CognitiveMemory.Application.Contracts;
using CognitiveMemory.Application.Interfaces;
using CognitiveMemory.Application.Services;
using CognitiveMemory.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using System.Linq;

namespace CognitiveMemory.Application.Tests;

public sealed class MemoryServiceInvariantTests
{
    private readonly MemoryService _service;

    public MemoryServiceInvariantTests()
    {
        _service = new MemoryService(
            new FakeDocumentRepository(),
            new FakeDocumentIngestionPipeline(),
            new FakeEmbeddingProvider(),
            new FakeClaimRepository(),
            new FakeQueryCache(),
            new FakeDebateOrchestrator(),
            new FakeHealthProbe(),
            NullLogger<MemoryService>.Instance);
    }

    [Fact]
    public async Task CreateClaimRequiresExactlyOneObjectOrLiteral()
    {
        var request = new CreateClaimRequest
        {
            SubjectEntityId = Guid.NewGuid(),
            Predicate = "selected_transport",
            ObjectEntityId = Guid.NewGuid(),
            LiteralValue = "SignalR",
            Evidence = [new CreateEvidenceRequest { SourceRef = "a", ExcerptOrSummary = "b" }]
        };

        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateClaimAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task CreateClaimRequiresEvidence()
    {
        var request = new CreateClaimRequest
        {
            SubjectEntityId = Guid.NewGuid(),
            Predicate = "selected_transport",
            LiteralValue = "SignalR"
        };

        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateClaimAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task IngestIsIdempotentForDuplicateSourceRef()
    {
        var documentRepo = new FakeDocumentRepository();
        var service = new MemoryService(
            documentRepo,
            new FakeDocumentIngestionPipeline(),
            new FakeEmbeddingProvider(),
            new FakeClaimRepository(),
            new FakeQueryCache(),
            new FakeDebateOrchestrator(),
            new FakeHealthProbe(),
            NullLogger<MemoryService>.Instance);

        var request = new IngestDocumentRequest
        {
            SourceType = "ChatTurn",
            SourceRef = "conv:1/turn:1",
            Content = "We switched to SignalR.",
            Metadata = new Dictionary<string, string> { ["project"] = "PokemonMMO" }
        };

        _ = await service.IngestDocumentAsync(request, CancellationToken.None);
        var second = await service.IngestDocumentAsync(request, CancellationToken.None);

        Assert.Equal("Queued", second.Status);
        Assert.Equal(0, second.ClaimsCreated);
    }

    private sealed class FakeHealthProbe : ISystemHealthProbe
    {
        public Task<MemoryHealthResponse> CheckAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new MemoryHealthResponse());
    }

    private sealed class FakeQueryCache : IQueryCache
    {
        public Task<QueryClaimsResponse?> GetAsync(string key, CancellationToken cancellationToken) => Task.FromResult<QueryClaimsResponse?>(null);

        public Task SetAsync(string key, QueryClaimsResponse value, TimeSpan ttl, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeDebateOrchestrator : IDebateOrchestrator
    {
        public Task<DebateResult> OrchestrateAsync(string question, QueryClaimsResponse memoryPacket, CancellationToken cancellationToken)
        {
            return Task.FromResult(new DebateResult
            {
                Answer = "Based on available evidence, it appears that the answer is SignalR.",
                Confidence = 0.8,
                Citations = [],
                UncertaintyFlags = [],
                Contradictions = []
            });
        }
    }

    private sealed class FakeDocumentIngestionPipeline : IDocumentIngestionPipeline
    {
        public Task<int> ProcessDocumentAsync(SourceDocument document, CancellationToken cancellationToken) => Task.FromResult(1);
    }

    private sealed class FakeEmbeddingProvider : ITextEmbeddingProvider
    {
        public Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken)
            => Task.FromResult(new ReadOnlyMemory<float>([0.5f, 0.5f, 0.2f, 0.1f]));
    }

    private sealed class FakeDocumentRepository : IDocumentRepository
    {
        private readonly Dictionary<string, SourceDocument> _documentsBySourceRef = new();

        public Task<SourceDocument?> GetByIdAsync(Guid documentId, CancellationToken cancellationToken) =>
            Task.FromResult(_documentsBySourceRef.Values.FirstOrDefault(d => d.DocumentId == documentId));

        public Task<SourceDocument?> GetBySourceRefAsync(string sourceType, string sourceRef, CancellationToken cancellationToken)
        {
            _documentsBySourceRef.TryGetValue($"{sourceType}:{sourceRef}", out var document);
            return Task.FromResult(document);
        }

        public Task<SourceDocument?> GetBySourceHashAsync(string sourceType, string sourceRef, string contentHash, CancellationToken cancellationToken) =>
            Task.FromResult<SourceDocument?>(null);

        public Task<SourceDocument> CreateAsync(string sourceType, string sourceRef, string content, string metadata, string contentHash, CancellationToken cancellationToken)
        {
            var document = new SourceDocument
            {
                DocumentId = Guid.NewGuid(),
                SourceType = sourceType,
                SourceRef = sourceRef,
                Content = content,
                ContentHash = contentHash,
                Metadata = metadata,
                CapturedAt = DateTimeOffset.UtcNow
            };

            _documentsBySourceRef[$"{sourceType}:{sourceRef}"] = document;
            return Task.FromResult(document);
        }
    }

    private sealed class FakeClaimRepository : IClaimRepository
    {
        public Task<IReadOnlyList<ClaimListItem>> GetRecentAsync(int take, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ClaimListItem>>([]);

        public Task<ClaimCreatedResponse> CreateAsync(CreateClaimRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new ClaimCreatedResponse { ClaimId = Guid.NewGuid(), Status = ClaimStatus.Active });

        public Task<Claim?> GetByHashAsync(string hash, CancellationToken cancellationToken) => Task.FromResult<Claim?>(null);

        public Task<IReadOnlyList<QueryCandidate>> GetQueryCandidatesAsync(string? subjectFilter, CancellationToken cancellationToken, int maxCandidates = 0) =>
            Task.FromResult<IReadOnlyList<QueryCandidate>>([]);

        public Task<QueryCandidate?> GetQueryCandidateByIdAsync(Guid claimId, CancellationToken cancellationToken) =>
            Task.FromResult<QueryCandidate?>(null);

        public Task<ClaimLifecycleResponse> SupersedeAsync(Guid claimId, Guid replacementClaimId, CancellationToken cancellationToken) =>
            Task.FromResult(new ClaimLifecycleResponse { ClaimId = claimId, Status = ClaimStatus.Superseded, UpdatedAt = DateTimeOffset.UtcNow });

        public Task<ClaimLifecycleResponse> RetractAsync(Guid claimId, CancellationToken cancellationToken) =>
            Task.FromResult(new ClaimLifecycleResponse { ClaimId = claimId, Status = ClaimStatus.Retracted, UpdatedAt = DateTimeOffset.UtcNow });

        public Task<Guid> CreateManualContradictionAsync(Guid claimId, string reason, CancellationToken cancellationToken) =>
            Task.FromResult(Guid.NewGuid());

        public Task<ClaimConfidenceUpdateResult?> TryApplyRecommendedConfidenceAsync(
            Guid claimId,
            double recommendedConfidence,
            double minDeltaToApply,
            double maxStep,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<ClaimConfidenceUpdateResult?>(new ClaimConfidenceUpdateResult
            {
                ClaimId = claimId,
                PreviousConfidence = 0.5,
                UpdatedConfidence = 0.5,
                Applied = false,
                Reason = "not_applicable_in_test_double"
            });
        }
    }
}
