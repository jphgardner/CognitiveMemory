using System.Text.RegularExpressions;
using CognitiveMemory.Application.Abstractions;
using CognitiveMemory.Application.Cognitive;
using CognitiveMemory.Domain.Memory;

namespace CognitiveMemory.Application.Reasoning;

public sealed partial class CognitiveReasoningService(
    IEpisodicMemoryRepository episodicRepository,
    ISemanticMemoryRepository semanticRepository,
    IProceduralMemoryRepository proceduralRepository,
    ICompanionDirectory companionDirectory,
    ICompanionCognitiveProfileResolver cognitiveProfileResolver,
    CognitiveReasoningOptions options) : ICognitiveReasoningService
{
    private static readonly Regex SemanticPattern = ClaimPattern();

    public async Task<CognitiveReasoningRunResult> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var startedAtUtc = DateTimeOffset.UtcNow;
        var fromUtc = startedAtUtc.AddHours(-Math.Max(1, options.LookbackHours));
        var companions = await companionDirectory.ListActiveAsync(cancellationToken);

        var episodesScanned = 0;
        var claimsScanned = 0;
        var inferredClaims = 0;
        var confidenceAdjustments = 0;
        var proceduralSuggestions = 0;
        var weakClaims = 0;

        foreach (var companion in companions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var profile = await ResolveProfileAsync(companion.CompanionId, cancellationToken);
            var episodes = await episodicRepository.QueryRangeAsync(
                companion.CompanionId,
                fromUtc,
                startedAtUtc,
                Math.Clamp(options.MaxEpisodesScanned, 10, 2000),
                cancellationToken);
            episodesScanned += episodes.Count;

            var activeClaims = await semanticRepository.QueryClaimsAsync(
                companion.CompanionId,
                status: SemanticClaimStatus.Active,
                take: Math.Clamp(options.MaxClaimsScanned, 10, 2000),
                cancellationToken: cancellationToken);
            claimsScanned += activeClaims.Count;
            weakClaims += activeClaims.Count(x => x.Confidence <= options.WeakClaimThreshold);
            var activeClaimIndex = BuildClaimIndex(activeClaims);

            var clusters = BuildClusters(episodes);
            foreach (var cluster in clusters.Values.Where(x => x.Occurrences >= Math.Max(2, options.MinPatternOccurrences)))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var inferredConfidence = Math.Clamp(
                    options.BaseInferenceConfidence + ((cluster.Occurrences - 1) * options.ConfidenceStepPerOccurrence),
                    0.05,
                    options.MaxInferredConfidence);
                inferredConfidence = Math.Clamp(
                    inferredConfidence * Math.Clamp(profile.Reasoning.EvidenceStrictness + 0.4, 0.5, 1.3),
                    0.05,
                    options.MaxInferredConfidence);

                var existing = activeClaimIndex.GetValueOrDefault(BuildKey(cluster.Subject, cluster.Predicate, cluster.Value));

                if (existing is null)
                {
                    var claim = CreateClaim(cluster.Subject, cluster.Predicate, cluster.Value, inferredConfidence, "reasoned");
                    var created = await semanticRepository.CreateClaimAsync(companion.CompanionId, claim, cancellationToken);
                    await semanticRepository.AddEvidenceAsync(
                        companion.CompanionId,
                        BuildEvidence(created.ClaimId, cluster),
                        cancellationToken);

                    inferredClaims++;
                    continue;
                }

                if ((inferredConfidence - existing.Confidence) < options.MinConfidenceDeltaForAdjustment)
                {
                    continue;
                }

                var strengthened = CreateClaim(
                    existing.Subject,
                    existing.Predicate,
                    existing.Value,
                    inferredConfidence,
                    existing.Scope,
                    existing.ValidFromUtc,
                    existing.ValidToUtc);

                var replacement = await semanticRepository.CreateClaimAsync(companion.CompanionId, strengthened, cancellationToken);
                await semanticRepository.SupersedeAsync(companion.CompanionId, existing.ClaimId, replacement.ClaimId, cancellationToken);
                await semanticRepository.AddEvidenceAsync(
                    companion.CompanionId,
                    BuildEvidence(replacement.ClaimId, cluster),
                    cancellationToken);
                confidenceAdjustments++;
            }

            if (options.SuggestProceduralPatterns)
            {
                proceduralSuggestions += await SuggestProceduralPatternsAsync(companion.CompanionId, episodes, profile, cancellationToken);
            }
        }

        return new CognitiveReasoningRunResult(
            episodesScanned,
            claimsScanned,
            inferredClaims,
            confidenceAdjustments,
            weakClaims,
            proceduralSuggestions,
            startedAtUtc,
            DateTimeOffset.UtcNow);
    }

    private async Task<int> SuggestProceduralPatternsAsync(
        Guid companionId,
        IReadOnlyList<EpisodicMemoryEvent> episodes,
        CompanionCognitiveProfileDocument profile,
        CancellationToken cancellationToken)
    {
        var signals = episodes
            .Where(x => string.Equals(x.Who, "user", StringComparison.OrdinalIgnoreCase))
            .Select(x => NormalizeIntent(x.What))
            .Where(x => x.Length >= 12)
            .GroupBy(x => x)
            .Select(x => new { Trigger = x.Key, Count = x.Count() })
            .Where(x => x.Count >= Math.Max(2, options.MinPatternOccurrences))
            .OrderByDescending(x => x.Count)
            .Take(5)
            .ToArray();

        var created = 0;
        foreach (var signal in signals)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var existing = await proceduralRepository.QueryByTriggerAsync(companionId, signal.Trigger, 1, cancellationToken);
            if (existing.Count > 0)
            {
                continue;
            }

            var now = DateTimeOffset.UtcNow;
            var strategy = profile.Adaptation.Procedurality >= profile.Adaptation.Adaptivity
                ? "Apply known workflow first, then adapt if constraints change."
                : "Adapt workflow dynamically to new constraints, then capture the stable pattern.";
            var routine = new ProceduralRoutine(
                Guid.NewGuid(),
                signal.Trigger,
                $"Routine: {signal.Trigger}",
                [
                    $"Reconstruct context for `{signal.Trigger}` from recent memory.",
                    strategy,
                    "Record execution outcome and learning back into memory."
                ],
                ["Outcome validated", "Follow-up captured"],
                "Repeated user behavior indicates this workflow is reusable.",
                now,
                now);

            await proceduralRepository.UpsertAsync(companionId, routine, cancellationToken);
            created++;
        }

        return created;
    }

    private static Dictionary<string, ClaimCluster> BuildClusters(IReadOnlyList<EpisodicMemoryEvent> episodes)
    {
        var clusters = new Dictionary<string, ClaimCluster>(StringComparer.Ordinal);

        foreach (var episode in episodes)
        {
            if (!TryExtractClaim(episode.What, out var subject, out var predicate, out var value))
            {
                continue;
            }

            var key = BuildKey(subject, predicate, value);
            if (!clusters.TryGetValue(key, out var cluster))
            {
                cluster = new ClaimCluster(subject, predicate, value);
                clusters[key] = cluster;
            }

            cluster.Occurrences++;

            if (cluster.SourceReferences.Count < 8)
            {
                cluster.SourceReferences.Add(episode.SourceReference);
            }

            if (cluster.Examples.Count < 8)
            {
                cluster.Examples.Add(TrimForSummary(episode.What));
            }
        }

        return clusters;
    }

    private static bool TryExtractClaim(string text, out string subject, out string predicate, out string value)
    {
        subject = string.Empty;
        predicate = string.Empty;
        value = string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var match = SemanticPattern.Match(text.Trim());
        if (!match.Success)
        {
            return false;
        }

        subject = match.Groups["subject"].Value.Trim();
        predicate = match.Groups["predicate"].Value.Trim().ToLowerInvariant();
        value = match.Groups["value"].Value.Trim().TrimEnd('.', '!', '?');
        return subject.Length > 0 && predicate.Length > 0 && value.Length > 0;
    }

    private static SemanticClaim CreateClaim(
        string subject,
        string predicate,
        string value,
        double confidence,
        string scope,
        DateTimeOffset? validFromUtc = null,
        DateTimeOffset? validToUtc = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new SemanticClaim(
            Guid.NewGuid(),
            subject,
            predicate,
            value,
            Math.Clamp(confidence, 0, 1),
            scope,
            SemanticClaimStatus.Active,
            validFromUtc,
            validToUtc,
            null,
            now,
            now);
    }

    private static ClaimEvidence BuildEvidence(Guid claimId, ClaimCluster cluster)
    {
        var sourceReference = cluster.SourceReferences.FirstOrDefault() ?? "reasoning:cluster";
        var excerpt = $"Clustered evidence ({cluster.Occurrences} occurrences): {string.Join(" | ", cluster.Examples.Take(3))}";

        return new ClaimEvidence(
            Guid.NewGuid(),
            claimId,
            "reasoning-cluster",
            sourceReference,
            excerpt,
            Math.Clamp(0.5 + (cluster.Occurrences * 0.08), 0.1, 0.95),
            DateTimeOffset.UtcNow);
    }

    private static Dictionary<string, SemanticClaim> BuildClaimIndex(IReadOnlyList<SemanticClaim> claims)
    {
        var index = new Dictionary<string, SemanticClaim>(StringComparer.Ordinal);
        foreach (var claim in claims)
        {
            var key = BuildKey(claim.Subject, claim.Predicate, claim.Value);
            if (!index.ContainsKey(key))
            {
                index[key] = claim;
            }
        }

        return index;
    }

    private static string NormalizeIntent(string input)
    {
        var cleaned = new string(
            input
                .ToLowerInvariant()
                .Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c))
                .ToArray());

        var tokens = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return string.Empty;
        }

        return string.Join(' ', tokens.Take(6));
    }

    private static string BuildKey(string subject, string predicate, string value) =>
        $"{subject.Trim().ToLowerInvariant()}|{predicate.Trim().ToLowerInvariant()}|{value.Trim().ToLowerInvariant()}";

    private static string TrimForSummary(string text)
    {
        if (text.Length <= 120)
        {
            return text;
        }

        return text[..120];
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

    [GeneratedRegex("^(?<subject>[^.?!]+?)\\s+(?<predicate>is|are|has|likes|prefers|uses|owns|works at|lives in|needs)\\s+(?<value>[^.?!]+)[.?!]?$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ClaimPattern();

    private sealed class ClaimCluster(string subject, string predicate, string value)
    {
        public string Subject { get; } = subject;
        public string Predicate { get; } = predicate;
        public string Value { get; } = value;
        public int Occurrences { get; set; }
        public List<string> SourceReferences { get; } = [];
        public List<string> Examples { get; } = [];
    }
}
