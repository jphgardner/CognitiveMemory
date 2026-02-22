using CognitiveMemory.Application.Abstractions;
using CognitiveMemory.Application.Cognitive;
using CognitiveMemory.Domain.Memory;

namespace CognitiveMemory.Application.Semantic;

public sealed class SemanticMemoryService(
    ISemanticMemoryRepository repository,
    ICompanionDirectory companionDirectory,
    ICompanionCognitiveProfileResolver cognitiveProfileResolver) : ISemanticMemoryService
{
    public Task<IReadOnlyList<SemanticClaim>> QueryClaimsAsync(
        string? subject = null,
        string? predicate = null,
        SemanticClaimStatus? status = null,
        int take = 100,
        CancellationToken cancellationToken = default)
        => repository.QueryClaimsAsync(subject?.Trim(), predicate?.Trim(), status, Math.Clamp(take, 1, 500), cancellationToken);

    public async Task<SemanticClaim> CreateClaimAsync(CreateSemanticClaimRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Subject)) throw new ArgumentException("Subject is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Predicate)) throw new ArgumentException("Predicate is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Value)) throw new ArgumentException("Value is required.", nameof(request));
        if (request.Confidence is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(request), "Confidence must be between 0 and 1.");

        var now = DateTimeOffset.UtcNow;
        var claim = new SemanticClaim(
            Guid.NewGuid(),
            request.Subject.Trim(),
            request.Predicate.Trim(),
            request.Value.Trim(),
            request.Confidence,
            string.IsNullOrWhiteSpace(request.Scope) ? "global" : request.Scope.Trim(),
            request.Status,
            request.ValidFromUtc,
            request.ValidToUtc,
            null,
            now,
            now);

        return await repository.CreateClaimAsync(claim, cancellationToken);
    }

    public async Task<SemanticClaim> SupersedeClaimAsync(SupersedeSemanticClaimRequest request, CancellationToken cancellationToken = default)
    {
        var oldClaim = await repository.GetByIdAsync(request.ClaimId, cancellationToken)
                       ?? throw new InvalidOperationException("Claim not found.");

        var now = DateTimeOffset.UtcNow;
        var newClaim = new SemanticClaim(
            Guid.NewGuid(),
            request.Subject.Trim(),
            request.Predicate.Trim(),
            request.Value.Trim(),
            Math.Clamp(request.Confidence, 0, 1),
            request.Scope.Trim(),
            SemanticClaimStatus.Active,
            null,
            null,
            null,
            now,
            now);

        var created = await repository.CreateClaimAsync(newClaim, cancellationToken);
        await repository.SupersedeAsync(oldClaim.ClaimId, created.ClaimId, cancellationToken);
        return created;
    }

    public async Task<ClaimEvidence> AddEvidenceAsync(AddClaimEvidenceRequest request, CancellationToken cancellationToken = default)
    {
        if (request.ClaimId == Guid.Empty) throw new ArgumentException("ClaimId is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.SourceType)) throw new ArgumentException("SourceType is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.SourceReference)) throw new ArgumentException("SourceReference is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.ExcerptOrSummary)) throw new ArgumentException("ExcerptOrSummary is required.", nameof(request));
        if (request.Strength is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(request), "Strength must be between 0 and 1.");

        var evidence = new ClaimEvidence(
            Guid.NewGuid(),
            request.ClaimId,
            request.SourceType.Trim(),
            request.SourceReference.Trim(),
            request.ExcerptOrSummary.Trim(),
            request.Strength,
            request.CapturedAtUtc ?? DateTimeOffset.UtcNow);

        return await repository.AddEvidenceAsync(evidence, cancellationToken);
    }

    public async Task<ClaimContradiction> AddContradictionAsync(AddClaimContradictionRequest request, CancellationToken cancellationToken = default)
    {
        if (request.ClaimAId == Guid.Empty || request.ClaimBId == Guid.Empty) throw new ArgumentException("Claim ids are required.", nameof(request));
        if (request.ClaimAId == request.ClaimBId) throw new ArgumentException("Contradiction must reference two different claims.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Type)) throw new ArgumentException("Type is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Severity)) throw new ArgumentException("Severity is required.", nameof(request));

        var contradiction = new ClaimContradiction(
            Guid.NewGuid(),
            request.ClaimAId,
            request.ClaimBId,
            request.Type.Trim(),
            request.Severity.Trim(),
            request.DetectedAtUtc ?? DateTimeOffset.UtcNow,
            string.IsNullOrWhiteSpace(request.Status) ? "Open" : request.Status.Trim());

        return await repository.AddContradictionAsync(contradiction, cancellationToken);
    }

    public async Task<int> RunDecayAsync(int staleDays, double decayStep, double minConfidence, CancellationToken cancellationToken = default)
    {
        var companions = await companionDirectory.ListActiveAsync(cancellationToken);
        var total = 0;
        foreach (var companion in companions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var profile = await ResolveProfileAsync(companion.CompanionId, cancellationToken);
            var staleBeforeUtc = DateTimeOffset.UtcNow.AddDays(-Math.Max(1, staleDays));
            var effectiveDecayStep = Math.Clamp(
                decayStep * Math.Clamp(profile.Memory.Decay.SemanticDailyDecay * 20, 0.5, 2.0),
                0.01,
                0.5);
            var affected = await repository.DecayActiveClaimsAsync(
                companion.CompanionId,
                staleBeforeUtc,
                effectiveDecayStep,
                Math.Clamp(minConfidence, 0, 1),
                cancellationToken);
            total += affected;
        }

        return total;
    }

    private async Task<CompanionCognitiveProfileDocument> ResolveProfileAsync(Guid companionId, CancellationToken cancellationToken)
    {
        try
        {
            return (await cognitiveProfileResolver.ResolveByCompanionIdAsync(companionId, cancellationToken)).Profile;
        }
        catch
        {
            return new CompanionCognitiveProfileDocument();
        }
    }
}
