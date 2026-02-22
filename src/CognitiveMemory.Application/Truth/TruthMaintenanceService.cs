using CognitiveMemory.Application.Abstractions;
using CognitiveMemory.Application.Cognitive;
using CognitiveMemory.Domain.Memory;

namespace CognitiveMemory.Application.Truth;

public sealed class TruthMaintenanceService(
    ISemanticMemoryRepository semanticRepository,
    ICompanionDirectory companionDirectory,
    ICompanionCognitiveProfileResolver cognitiveProfileResolver,
    TruthMaintenanceOptions options) : ITruthMaintenanceService
{
    public async Task<TruthMaintenanceRunResult> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var startedAtUtc = DateTimeOffset.UtcNow;
        var companions = await companionDirectory.ListActiveAsync(cancellationToken);

        var claimsScanned = 0;
        var conflictClusters = 0;
        var contradictionsRecorded = 0;
        var confidenceAdjustments = 0;
        var probabilisticMarks = 0;
        var clarificationRequests = new List<string>();

        foreach (var companion in companions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var profile = await ResolveProfileAsync(companion.CompanionId, cancellationToken);
            var activeClaims = await semanticRepository.QueryClaimsAsync(
                companion.CompanionId,
                status: SemanticClaimStatus.Active,
                take: Math.Clamp(options.MaxClaimsScanned, 20, 2000),
                cancellationToken: cancellationToken);
            claimsScanned += activeClaims.Count;

            var conflicts = activeClaims
                .GroupBy(x => $"{Normalize(x.Subject)}|{Normalize(x.Predicate)}")
                .Select(x => x.ToArray())
                .Where(x => x.Select(c => Normalize(c.Value)).Distinct(StringComparer.Ordinal).Count() > 1)
                .ToArray();
            conflictClusters += conflicts.Length;

            var processedPairs = 0;
            var supersededClaimIds = new HashSet<Guid>();
            var conflictPenalty = Math.Max(
                options.ConflictConfidencePenalty,
                Math.Clamp(1d - profile.Reasoning.EvidenceStrictness, 0.05, 0.45));
            var uncertainThreshold = Math.Min(
                options.UncertainThreshold,
                profile.Uncertainty.ClarifyConfidenceThreshold);

            foreach (var cluster in conflicts)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (processedPairs >= Math.Max(1, options.MaxConflictPairsPerRun))
                {
                    break;
                }

                for (var i = 0; i < cluster.Length; i++)
                {
                    if (processedPairs >= Math.Max(1, options.MaxConflictPairsPerRun))
                    {
                        break;
                    }

                    for (var j = i + 1; j < cluster.Length; j++)
                    {
                        if (processedPairs >= Math.Max(1, options.MaxConflictPairsPerRun))
                        {
                            break;
                        }

                        var left = cluster[i];
                        var right = cluster[j];
                        if (Normalize(left.Value) == Normalize(right.Value))
                        {
                            continue;
                        }

                        await semanticRepository.AddContradictionAsync(
                            companion.CompanionId,
                            new ClaimContradiction(
                                Guid.NewGuid(),
                                left.ClaimId,
                                right.ClaimId,
                                "value-conflict",
                                InferSeverity(left, right),
                                DateTimeOffset.UtcNow,
                                "Open"),
                            cancellationToken);

                        contradictionsRecorded++;
                        processedPairs++;
                    }
                }

                var clarification = $"Clarify `{cluster[0].Subject} {cluster[0].Predicate}` because values conflict: {string.Join(" vs ", cluster.Select(x => x.Value).Distinct(StringComparer.OrdinalIgnoreCase).Take(3))}";
                clarificationRequests.Add(clarification);

                foreach (var claim in cluster)
                {
                    if (claim.Scope.Contains("probabilistic", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var adjustedConfidence = Math.Max(0, claim.Confidence - conflictPenalty);
                    if ((claim.Confidence - adjustedConfidence) < options.MinConfidenceDeltaForAdjustment)
                    {
                        continue;
                    }

                    var adjustedScope = adjustedConfidence <= uncertainThreshold
                        ? EnsureProbabilisticScope(claim.Scope)
                        : claim.Scope;

                    var replacement = await semanticRepository.CreateClaimAsync(
                        companion.CompanionId,
                        CreateReplacementClaim(claim, adjustedConfidence, adjustedScope),
                        cancellationToken);

                    await semanticRepository.SupersedeAsync(companion.CompanionId, claim.ClaimId, replacement.ClaimId, cancellationToken);
                    supersededClaimIds.Add(claim.ClaimId);

                    confidenceAdjustments++;
                    if (adjustedScope != claim.Scope)
                    {
                        probabilisticMarks++;
                    }
                }
            }

            foreach (var claim in activeClaims)
            {
                if (supersededClaimIds.Contains(claim.ClaimId))
                {
                    continue;
                }

                if (claim.Confidence > uncertainThreshold)
                {
                    continue;
                }

                if (claim.Scope.Contains("probabilistic", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var replacement = await semanticRepository.CreateClaimAsync(
                    companion.CompanionId,
                    CreateReplacementClaim(claim, claim.Confidence, EnsureProbabilisticScope(claim.Scope)),
                    cancellationToken);

                await semanticRepository.SupersedeAsync(companion.CompanionId, claim.ClaimId, replacement.ClaimId, cancellationToken);
                supersededClaimIds.Add(claim.ClaimId);
                probabilisticMarks++;
            }
        }

        return new TruthMaintenanceRunResult(
            claimsScanned,
            conflictClusters,
            contradictionsRecorded,
            confidenceAdjustments,
            probabilisticMarks,
            clarificationRequests.Take(10).ToArray(),
            startedAtUtc,
            DateTimeOffset.UtcNow);
    }

    private static SemanticClaim CreateReplacementClaim(SemanticClaim claim, double confidence, string scope)
    {
        var now = DateTimeOffset.UtcNow;
        return new SemanticClaim(
            Guid.NewGuid(),
            claim.Subject,
            claim.Predicate,
            claim.Value,
            Math.Clamp(confidence, 0, 1),
            scope,
            SemanticClaimStatus.Active,
            claim.ValidFromUtc,
            claim.ValidToUtc,
            null,
            now,
            now);
    }

    private static string EnsureProbabilisticScope(string scope)
    {
        if (scope.Contains("probabilistic", StringComparison.OrdinalIgnoreCase))
        {
            return scope;
        }

        return $"probabilistic:{scope}";
    }

    private static string InferSeverity(SemanticClaim left, SemanticClaim right)
    {
        var delta = Math.Abs(left.Confidence - right.Confidence);
        if (delta >= 0.5)
        {
            return "High";
        }

        if (delta >= 0.2)
        {
            return "Medium";
        }

        return "Low";
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();

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
