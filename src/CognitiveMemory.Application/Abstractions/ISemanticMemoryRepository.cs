using CognitiveMemory.Domain.Memory;

namespace CognitiveMemory.Application.Abstractions;

public interface ISemanticMemoryRepository
{
    Task<SemanticClaim> CreateClaimAsync(SemanticClaim claim, CancellationToken cancellationToken = default);
    Task<SemanticClaim?> GetByIdAsync(Guid claimId, CancellationToken cancellationToken = default);
    Task SupersedeAsync(Guid claimId, Guid supersededByClaimId, CancellationToken cancellationToken = default);
    Task<ClaimEvidence> AddEvidenceAsync(ClaimEvidence evidence, CancellationToken cancellationToken = default);
    Task<ClaimContradiction> AddContradictionAsync(ClaimContradiction contradiction, CancellationToken cancellationToken = default);
    Task<int> DecayActiveClaimsAsync(DateTimeOffset staleBeforeUtc, double decayStep, double minConfidence, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SemanticClaim>> QueryClaimsAsync(
        string? subject = null,
        string? predicate = null,
        SemanticClaimStatus? status = null,
        int take = 100,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SemanticClaim>> SearchClaimsAsync(
        string query,
        int take = 100,
        CancellationToken cancellationToken = default);
}
