using CognitiveMemory.Application.Contracts;
using CognitiveMemory.Domain.Entities;

namespace CognitiveMemory.Application.Interfaces;

public interface IClaimRepository
{
    Task<IReadOnlyList<ClaimListItem>> GetRecentAsync(int take, CancellationToken cancellationToken);

    Task<ClaimCreatedResponse> CreateAsync(CreateClaimRequest request, CancellationToken cancellationToken);

    Task<Claim?> GetByHashAsync(string hash, CancellationToken cancellationToken);

    Task<IReadOnlyList<QueryCandidate>> GetQueryCandidatesAsync(string? subjectFilter, CancellationToken cancellationToken, int maxCandidates = 0);

    Task<QueryCandidate?> GetQueryCandidateByIdAsync(Guid claimId, CancellationToken cancellationToken);

    Task<ClaimLifecycleResponse> SupersedeAsync(Guid claimId, Guid replacementClaimId, CancellationToken cancellationToken);

    Task<ClaimLifecycleResponse> RetractAsync(Guid claimId, CancellationToken cancellationToken);

    Task<Guid> CreateManualContradictionAsync(Guid claimId, string reason, CancellationToken cancellationToken);

    Task<ClaimConfidenceUpdateResult?> TryApplyRecommendedConfidenceAsync(
        Guid claimId,
        double recommendedConfidence,
        double minDeltaToApply,
        double maxStep,
        CancellationToken cancellationToken);
}

public sealed class ClaimConfidenceUpdateResult
{
    public Guid ClaimId { get; init; }

    public double PreviousConfidence { get; init; }

    public double UpdatedConfidence { get; init; }

    public bool Applied { get; init; }

    public string Reason { get; init; } = string.Empty;
}
