using System.Text.RegularExpressions;
using CognitiveMemory.Application.Abstractions;
using CognitiveMemory.Domain.Memory;

namespace CognitiveMemory.Application.Consolidation;

public sealed partial class ConsolidationService(
    IEpisodicMemoryRepository episodicRepository,
    ISemanticMemoryRepository semanticRepository,
    IConsolidationStateRepository consolidationStateRepository,
    IClaimExtractionGateway claimExtractionGateway,
    ConsolidationOptions options) : IConsolidationService
{
    private static readonly Regex FactPattern = SemanticPattern();

    public async Task<ConsolidationRunResult> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var started = DateTimeOffset.UtcNow;
        var from = started.AddHours(-Math.Max(1, options.LookbackHours));
        var candidates = await episodicRepository.QueryRangeAsync(from, started, Math.Max(1, options.MaxCandidatesPerRun), cancellationToken);

        var processed = 0;
        var promoted = 0;
        var skipped = 0;

        foreach (var candidate in candidates.OrderBy(x => x.OccurredAt))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await consolidationStateRepository.IsProcessedAsync(candidate.EventId, cancellationToken))
            {
                skipped++;
                continue;
            }

            var claim = await TryExtractClaimAsync(candidate, cancellationToken);
            if (claim is null)
            {
                await consolidationStateRepository.MarkProcessedAsync(candidate.EventId, "NoExtractableClaim", notes: candidate.What, cancellationToken: cancellationToken);
                processed++;
                skipped++;
                continue;
            }

            if (claim.Confidence < options.MinExtractionConfidence)
            {
                await consolidationStateRepository.MarkProcessedAsync(
                    candidate.EventId,
                    "BelowConfidenceThreshold",
                    notes: $"confidence={claim.Confidence:F2}",
                    cancellationToken: cancellationToken);
                processed++;
                skipped++;
                continue;
            }

            var occurrenceCount = CountOccurrences(candidates, claim);
            if (occurrenceCount < Math.Max(1, options.MinOccurrencesForPromotion))
            {
                await consolidationStateRepository.MarkProcessedAsync(
                    candidate.EventId,
                    "InsufficientOccurrences",
                    notes: $"occurrences={occurrenceCount}",
                    cancellationToken: cancellationToken);
                processed++;
                skipped++;
                continue;
            }

            var existing = await semanticRepository.QueryClaimsAsync(
                claim.Subject,
                claim.Predicate,
                SemanticClaimStatus.Active,
                25,
                cancellationToken);

            var duplicate = existing.Any(x => string.Equals(x.Value, claim.Value, StringComparison.OrdinalIgnoreCase));
            if (duplicate)
            {
                await consolidationStateRepository.MarkProcessedAsync(candidate.EventId, "DuplicateClaim", notes: $"{claim.Subject}|{claim.Predicate}|{claim.Value}", cancellationToken: cancellationToken);
                processed++;
                skipped++;
                continue;
            }

            var created = await semanticRepository.CreateClaimAsync(claim, cancellationToken);
            await semanticRepository.AddEvidenceAsync(
                new ClaimEvidence(
                    Guid.NewGuid(),
                    created.ClaimId,
                    "episodic",
                    candidate.SourceReference,
                    candidate.What,
                    0.65,
                    DateTimeOffset.UtcNow),
                cancellationToken);

            await consolidationStateRepository.MarkProcessedAsync(
                candidate.EventId,
                "Promoted",
                created.ClaimId,
                candidate.What,
                cancellationToken);

            processed++;
            promoted++;
        }

        return new ConsolidationRunResult(
            candidates.Count,
            processed,
            promoted,
            skipped,
            started,
            DateTimeOffset.UtcNow);
    }

    private async Task<SemanticClaim?> TryExtractClaimAsync(EpisodicMemoryEvent input, CancellationToken cancellationToken)
    {
        var extracted = await claimExtractionGateway.ExtractAsync(input.What, cancellationToken);
        if (extracted is not null)
        {
            var now = DateTimeOffset.UtcNow;
            return new SemanticClaim(
                Guid.NewGuid(),
                extracted.Subject,
                extracted.Predicate,
                extracted.Value,
                Math.Clamp(extracted.Confidence, 0, 1),
                "consolidated",
                SemanticClaimStatus.Active,
                null,
                null,
                null,
                now,
                now);
        }

        var match = FactPattern.Match(input.What);
        if (!match.Success)
        {
            return null;
        }

        var subject = match.Groups["subject"].Value.Trim();
        var predicate = match.Groups["predicate"].Value.Trim();
        var value = match.Groups["value"].Value.Trim().TrimEnd('.', '!', '?');

        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(predicate) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var createdAt = DateTimeOffset.UtcNow;
        return new SemanticClaim(
            Guid.NewGuid(),
            subject,
            predicate,
            value,
            0.6,
            "consolidated",
            SemanticClaimStatus.Active,
            null,
            null,
            null,
            createdAt,
            createdAt);
    }

    private static int CountOccurrences(IReadOnlyList<EpisodicMemoryEvent> candidates, SemanticClaim claim)
    {
        var subject = Normalize(claim.Subject);
        var predicate = Normalize(claim.Predicate);
        var value = Normalize(claim.Value);
        var count = 0;

        foreach (var candidate in candidates)
        {
            var normalized = Normalize(candidate.What);
            if (normalized.Contains(subject, StringComparison.Ordinal)
                && normalized.Contains(predicate, StringComparison.Ordinal)
                && normalized.Contains(value, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    private static string Normalize(string input) =>
        new(input
            .ToLowerInvariant()
            .Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || c == '|')
            .ToArray());

    [GeneratedRegex("^(?<subject>[^.?!]+?)\\s+(?<predicate>is|has|likes|owns|works at|lives in)\\s+(?<value>[^.?!]+)[.?!]?$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SemanticPattern();
}
