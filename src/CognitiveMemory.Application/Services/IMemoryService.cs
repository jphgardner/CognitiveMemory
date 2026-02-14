using CognitiveMemory.Application.Contracts;

namespace CognitiveMemory.Application.Services;

public interface IMemoryService
{
    Task<MemoryHealthResponse> GetHealthAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ClaimListItem>> GetClaimsAsync(CancellationToken cancellationToken);

    Task<ClaimCreatedResponse> CreateClaimAsync(CreateClaimRequest request, CancellationToken cancellationToken);

    Task<ClaimLifecycleResponse> SupersedeClaimAsync(Guid claimId, Guid replacementClaimId, CancellationToken cancellationToken);

    Task<ClaimLifecycleResponse> RetractClaimAsync(Guid claimId, CancellationToken cancellationToken);

    Task<IngestDocumentResponse> IngestDocumentAsync(IngestDocumentRequest request, CancellationToken cancellationToken);

    Task<QueryClaimsResponse> QueryClaimsAsync(QueryClaimsRequest request, string requestId, CancellationToken cancellationToken);

    Task<AnswerQuestionResponse> AnswerAsync(AnswerQuestionRequest request, string requestId, CancellationToken cancellationToken);
}
