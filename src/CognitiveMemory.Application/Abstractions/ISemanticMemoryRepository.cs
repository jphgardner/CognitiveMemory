using CognitiveMemory.Domain.Memory;

namespace CognitiveMemory.Application.Abstractions;

public interface ISemanticMemoryRepository
{
    Task<SemanticClaim> CreateClaimAsync(SemanticClaim claim, CancellationToken cancellationToken = default);
    Task<SemanticClaim> CreateClaimAsync(Guid companionId, SemanticClaim claim, CancellationToken cancellationToken = default)
        => CreateClaimAsync(claim, cancellationToken);
    Task<SemanticClaim?> GetByIdAsync(Guid claimId, CancellationToken cancellationToken = default);
    Task<SemanticClaim?> GetByIdAsync(Guid companionId, Guid claimId, CancellationToken cancellationToken = default)
        => GetByIdAsync(claimId, cancellationToken);
    Task SupersedeAsync(Guid claimId, Guid supersededByClaimId, CancellationToken cancellationToken = default);
    Task SupersedeAsync(Guid companionId, Guid claimId, Guid supersededByClaimId, CancellationToken cancellationToken = default)
        => SupersedeAsync(claimId, supersededByClaimId, cancellationToken);
    Task<ClaimEvidence> AddEvidenceAsync(ClaimEvidence evidence, CancellationToken cancellationToken = default);
    Task<ClaimEvidence> AddEvidenceAsync(Guid companionId, ClaimEvidence evidence, CancellationToken cancellationToken = default)
        => AddEvidenceAsync(evidence, cancellationToken);
    Task<ClaimContradiction> AddContradictionAsync(ClaimContradiction contradiction, CancellationToken cancellationToken = default);
    Task<ClaimContradiction> AddContradictionAsync(Guid companionId, ClaimContradiction contradiction, CancellationToken cancellationToken = default)
        => AddContradictionAsync(contradiction, cancellationToken);
    Task<int> DecayActiveClaimsAsync(DateTimeOffset staleBeforeUtc, double decayStep, double minConfidence, CancellationToken cancellationToken = default);
    Task<int> DecayActiveClaimsAsync(Guid companionId, DateTimeOffset staleBeforeUtc, double decayStep, double minConfidence, CancellationToken cancellationToken = default)
        => DecayActiveClaimsAsync(staleBeforeUtc, decayStep, minConfidence, cancellationToken);

    Task<IReadOnlyList<SemanticClaim>> QueryClaimsAsync(
        string? subject = null,
        string? predicate = null,
        SemanticClaimStatus? status = null,
        int take = 100,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SemanticClaim>> QueryClaimsAsync(
        Guid companionId,
        string? subject = null,
        string? predicate = null,
        SemanticClaimStatus? status = null,
        int take = 100,
        CancellationToken cancellationToken = default)
        => QueryClaimsAsync(subject, predicate, status, take, cancellationToken);

    Task<IReadOnlyList<SemanticClaim>> SearchClaimsAsync(
        string query,
        int take = 100,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SemanticClaim>> SearchClaimsAsync(
        Guid companionId,
        string query,
        int take = 100,
        CancellationToken cancellationToken = default)
        => SearchClaimsAsync(query, take, cancellationToken);
}
