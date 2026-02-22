using CognitiveMemory.Application.Abstractions;
using CognitiveMemory.Application.Cognitive;
using CognitiveMemory.Domain.Memory;

namespace CognitiveMemory.Application.Identity;

public sealed class IdentityEvolutionService(
    IEpisodicMemoryRepository episodicRepository,
    ISemanticMemoryRepository semanticRepository,
    IProceduralMemoryRepository proceduralRepository,
    ISelfModelRepository selfModelRepository,
    ICompanionDirectory companionDirectory,
    ICompanionCognitiveProfileResolver cognitiveProfileResolver,
    IdentityEvolutionOptions options) : IIdentityEvolutionService
{
    public async Task<IdentityEvolutionRunResult> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var startedAtUtc = DateTimeOffset.UtcNow;
        var now = DateTimeOffset.UtcNow;
        var companions = await companionDirectory.ListActiveAsync(cancellationToken);

        var episodesScanned = 0;
        var claimsScanned = 0;
        var routinesScanned = 0;
        var updatedKeys = new List<string>();

        foreach (var companion in companions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var profile = await ResolveProfileAsync(companion.CompanionId, cancellationToken);
            var episodes = await episodicRepository.QueryRangeAsync(
                companion.CompanionId,
                now.AddDays(-Math.Max(1, options.LookbackDays)),
                now,
                Math.Clamp(options.MaxEpisodesScanned, 50, 4000),
                cancellationToken);
            episodesScanned += episodes.Count;

            var activeClaims = await semanticRepository.QueryClaimsAsync(
                companion.CompanionId,
                status: SemanticClaimStatus.Active,
                take: 500,
                cancellationToken: cancellationToken);
            claimsScanned += activeClaims.Count;

            var recentRoutines = await proceduralRepository.QueryRecentAsync(companion.CompanionId, 50, cancellationToken);
            routinesScanned += recentRoutines.Count;
            var snapshot = await selfModelRepository.GetAsync(companion.CompanionId, cancellationToken);
            var current = snapshot.Preferences.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);

            var focus = InferProjectFocus(episodes, options.MinSignalOccurrences);
            if (focus is not null)
            {
                await SetIfChangedAsync(companion.CompanionId, options.FocusPreferenceKey, focus, current, updatedKeys, cancellationToken);
            }

            var style = InferCollaborationStyle(episodes, options.MinSignalOccurrences);
            if (style is not null)
            {
                await SetIfChangedAsync(companion.CompanionId, options.StylePreferenceKey, style, current, updatedKeys, cancellationToken);
            }

            var longTermGoal = InferLongTermGoal(activeClaims, recentRoutines);
            if (longTermGoal is not null)
            {
                await SetIfChangedAsync(companion.CompanionId, options.GoalPreferenceKey, longTermGoal, current, updatedKeys, cancellationToken);
            }

            if (profile.Expression.ToneStyle.Contains("coach", StringComparison.OrdinalIgnoreCase))
            {
                await SetIfChangedAsync(
                    companion.CompanionId,
                    "identity.communication_style",
                    "coaching",
                    current,
                    updatedKeys,
                    cancellationToken);
            }
        }

        return new IdentityEvolutionRunResult(
            episodesScanned,
            claimsScanned,
            routinesScanned,
            updatedKeys.Count,
            updatedKeys,
            startedAtUtc,
            DateTimeOffset.UtcNow);
    }

    private async Task SetIfChangedAsync(
        Guid companionId,
        string key,
        string value,
        IReadOnlyDictionary<string, string> current,
        ICollection<string> updatedKeys,
        CancellationToken cancellationToken)
    {
        if (current.TryGetValue(key, out var existing)
            && string.Equals(existing, value, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await selfModelRepository.SetPreferenceAsync(companionId, key, value, cancellationToken);
        updatedKeys.Add($"{companionId:N}:{key}");
    }

    private static string? InferProjectFocus(IReadOnlyList<EpisodicMemoryEvent> episodes, int minOccurrences)
    {
        var topicTerms = episodes
            .SelectMany(x => Tokenize(x.What))
            .Where(x => x.Length >= 4)
            .Where(x => !StopWords.Contains(x))
            .GroupBy(x => x)
            .Select(x => new { Topic = x.Key, Count = x.Count() })
            .Where(x => x.Count >= Math.Max(2, minOccurrences))
            .OrderByDescending(x => x.Count)
            .ToArray();

        return topicTerms.FirstOrDefault()?.Topic;
    }

    private static string? InferCollaborationStyle(IReadOnlyList<EpisodicMemoryEvent> episodes, int minOccurrences)
    {
        var strategicSignals = 0;
        var executionSignals = 0;

        foreach (var episode in episodes)
        {
            var normalized = episode.What.ToLowerInvariant();
            if (normalized.Contains("architecture", StringComparison.Ordinal)
                || normalized.Contains("plan", StringComparison.Ordinal)
                || normalized.Contains("design", StringComparison.Ordinal)
                || normalized.Contains("refactor", StringComparison.Ordinal)
                || normalized.Contains("strategy", StringComparison.Ordinal))
            {
                strategicSignals++;
            }

            if (normalized.Contains("implement", StringComparison.Ordinal)
                || normalized.Contains("fix", StringComparison.Ordinal)
                || normalized.Contains("test", StringComparison.Ordinal)
                || normalized.Contains("deploy", StringComparison.Ordinal)
                || normalized.Contains("run", StringComparison.Ordinal))
            {
                executionSignals++;
            }
        }

        var threshold = Math.Max(2, minOccurrences);
        if (strategicSignals >= threshold && strategicSignals >= executionSignals)
        {
            return "strategic-engineering";
        }

        if (executionSignals >= threshold)
        {
            return "execution-driven";
        }

        return null;
    }

    private static string? InferLongTermGoal(
        IReadOnlyList<SemanticClaim> claims,
        IReadOnlyList<ProceduralRoutine> routines)
    {
        var goalClaim = claims
            .Where(x => x.Confidence >= 0.7)
            .Where(x => x.Predicate.Contains("goal", StringComparison.OrdinalIgnoreCase)
                        || x.Predicate.Contains("focus", StringComparison.OrdinalIgnoreCase)
                        || x.Predicate.Contains("priority", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefault();

        if (goalClaim is not null)
        {
            return goalClaim.Value;
        }

        var routineSignal = routines
            .GroupBy(x => x.Trigger, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(x => x.Count())
            .ThenByDescending(x => x.Max(r => r.UpdatedAtUtc))
            .Select(x => x.Key)
            .FirstOrDefault();

        return routineSignal;
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        var normalized = new string(
            text
                .ToLowerInvariant()
                .Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c))
                .ToArray());

        return normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
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

    private static readonly HashSet<string> StopWords =
    [
        "the", "and", "with", "from", "that", "this", "have", "will", "would", "about", "into", "just", "then", "they",
        "there", "their", "what", "when", "where", "which", "while", "your", "you", "our", "for", "not", "are", "was"
    ];
}
