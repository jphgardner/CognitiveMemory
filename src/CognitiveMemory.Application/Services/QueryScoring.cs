using CognitiveMemory.Application.Contracts;

namespace CognitiveMemory.Application.Services;

internal static class QueryScoring
{
    private const double WBaseConfidence = 0.15;
    private const double WEvidenceStrength = 0.22;
    private const double WRetrievalRelevance = 0.28;
    private const double WRecencyBoost = 0.10;
    private const double WScopeMatchBoost = 0.10;
    private const double WContradictionPenalty = 0.10;
    private const double WStalenessPenalty = 0.05;

    private const double TopScoreDeltaThreshold = 0.05;
    private const double WeakEvidenceThreshold = 0.50;

    public static double ComputeScore(
        double baseConfidence,
        double evidenceStrength,
        double retrievalRelevance,
        double recencyBoost,
        double scopeMatchBoost,
        double contradictionPenalty,
        double stalenessPenalty)
    {
        var score =
            (WBaseConfidence * baseConfidence) +
            (WEvidenceStrength * evidenceStrength) +
            (WRetrievalRelevance * retrievalRelevance) +
            (WRecencyBoost * recencyBoost) +
            (WScopeMatchBoost * scopeMatchBoost) -
            (WContradictionPenalty * contradictionPenalty) -
            (WStalenessPenalty * stalenessPenalty);

        return Math.Round(score, 4);
    }

    public static double ComputeRecencyBoost(DateTimeOffset? when)
    {
        if (when is null)
        {
            return 0;
        }

        var days = (DateTimeOffset.UtcNow - when.Value).TotalDays;
        return Math.Clamp(Math.Exp(-days / 30), 0, 1);
    }

    public static double ComputeStalenessPenalty(DateTimeOffset? validTo)
    {
        if (validTo is null)
        {
            return 0;
        }

        return validTo.Value < DateTimeOffset.UtcNow ? 1 : 0;
    }

    public static double ComputeScopeMatchBoost(string? subjectFilter, string scope)
    {
        if (string.IsNullOrWhiteSpace(subjectFilter))
        {
            return 0;
        }

        return scope.Contains(subjectFilter, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
    }

    public static double ComputeContradictionPenalty(IReadOnlyList<QueryContradictionItem> contradictions)
    {
        if (contradictions.Count == 0)
        {
            return 0;
        }

        var penalties = contradictions
            .Where(c => string.Equals(c.Status, "Open", StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Severity.ToLowerInvariant() switch
            {
                "critical" => 1.0,
                "high" => 0.8,
                "medium" => 0.5,
                "low" => 0.2,
                _ => 0.3
            })
            .DefaultIfEmpty(0)
            .ToList();

        return penalties.Count == 0 ? 0 : penalties.Average();
    }

    public static IReadOnlyList<string> ComputeUncertaintyFlags(IReadOnlyList<QueryClaimItem> orderedClaims)
    {
        var flags = new List<string>();
        if (orderedClaims.Count == 0)
        {
            flags.Add("NoCandidateClaims");
            return flags;
        }

        if (orderedClaims.Count > 1)
        {
            var delta = orderedClaims[0].Score - orderedClaims[1].Score;
            if (delta < TopScoreDeltaThreshold)
            {
                flags.Add("CloseTopScores");
            }
        }

        var top = orderedClaims[0];
        if (top.Contradictions.Any(c =>
                string.Equals(c.Status, "Open", StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(c.Severity, "High", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(c.Severity, "Critical", StringComparison.OrdinalIgnoreCase))))
        {
            flags.Add("TopClaimHasSevereContradiction");
        }

        if (top.Evidence.Count < 1 || top.Evidence.Average(e => e.Strength) < WeakEvidenceThreshold)
        {
            flags.Add("WeakEvidenceSupport");
        }

        return flags;
    }
}
