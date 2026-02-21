using CognitiveMemory.Domain.Memory;

namespace CognitiveMemory.Application.Semantic;

public interface ISemanticMemoryService
{
    Task<SemanticClaim> CreateClaimAsync(CreateSemanticClaimRequest request, CancellationToken cancellationToken = default);
    Task<SemanticClaim> SupersedeClaimAsync(SupersedeSemanticClaimRequest request, CancellationToken cancellationToken = default);
    Task<ClaimEvidence> AddEvidenceAsync(AddClaimEvidenceRequest request, CancellationToken cancellationToken = default);
    Task<ClaimContradiction> AddContradictionAsync(AddClaimContradictionRequest request, CancellationToken cancellationToken = default);
    Task<int> RunDecayAsync(int staleDays, double decayStep, double minConfidence, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SemanticClaim>> QueryClaimsAsync(
        string? subject = null,
        string? predicate = null,
        SemanticClaimStatus? status = null,
        int take = 100,
        CancellationToken cancellationToken = default);
}
