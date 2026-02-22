using CognitiveMemory.Application.Abstractions;
using CognitiveMemory.Domain.Memory;
using CognitiveMemory.Infrastructure.Companions;
using Microsoft.Extensions.Logging;

namespace CognitiveMemory.Infrastructure.Subconscious;

public sealed class SubconsciousOutcomeApplier(
    ISemanticMemoryRepository semanticMemoryRepository,
    IProceduralMemoryRepository proceduralMemoryRepository,
    ISelfModelRepository selfModelRepository,
    ICompanionScopeResolver companionScopeResolver,
    ILogger<SubconsciousOutcomeApplier> logger) : ISubconsciousOutcomeApplier
{
    public Task<SubconsciousApplyReport> PreviewAsync(Guid debateId, string sessionId, SubconsciousDebateOutcome outcome, CancellationToken cancellationToken = default)
        => ApplyInternalAsync(debateId, sessionId, outcome, dryRun: true, cancellationToken);

    public Task<SubconsciousApplyReport> ApplyAsync(Guid debateId, string sessionId, SubconsciousDebateOutcome outcome, CancellationToken cancellationToken = default)
        => ApplyInternalAsync(debateId, sessionId, outcome, dryRun: false, cancellationToken);

    private async Task<SubconsciousApplyReport> ApplyInternalAsync(
        Guid debateId,
        string sessionId,
        SubconsciousDebateOutcome outcome,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var companionId = await companionScopeResolver.ResolveCompanionIdOrThrowAsync(sessionId, cancellationToken);
        var skips = new List<SubconsciousApplySkip>();
        var appliedClaimsToCreate = 0;
        var appliedClaimsToSupersede = 0;
        var appliedProcedural = 0;
        var appliedSelf = 0;

        foreach (var claim in outcome.ClaimsToCreate)
        {
            if (claim.Confidence < 0.65)
            {
                skips.Add(new SubconsciousApplySkip("semantic.create", $"{claim.Subject}.{claim.Predicate}", "confidence_below_min_0.65", claim.Confidence));
                continue;
            }

            if (!dryRun)
            {
                await semanticMemoryRepository.CreateClaimAsync(
                    companionId,
                    new SemanticClaim(
                        Guid.NewGuid(),
                        claim.Subject,
                        claim.Predicate,
                        claim.Value,
                        claim.Confidence,
                        string.IsNullOrWhiteSpace(claim.Scope) ? "global" : claim.Scope,
                        SemanticClaimStatus.Active,
                        null,
                        null,
                        null,
                        now,
                        now),
                    cancellationToken);
            }
            appliedClaimsToCreate += 1;
        }

        foreach (var supersede in outcome.ClaimsToSupersede)
        {
            var existing = await semanticMemoryRepository.GetByIdAsync(companionId, supersede.ClaimId, cancellationToken);
            if (existing is null)
            {
                skips.Add(new SubconsciousApplySkip("semantic.supersede", supersede.ClaimId.ToString("D"), "existing_claim_not_found", supersede.Replacement.Confidence));
                continue;
            }

            if (supersede.Replacement.Confidence <= existing.Confidence + 0.08)
            {
                skips.Add(new SubconsciousApplySkip("semantic.supersede", supersede.ClaimId.ToString("D"), "replacement_confidence_delta_below_min_0.08", supersede.Replacement.Confidence));
                continue;
            }

            if (!dryRun)
            {
                var replacement = await semanticMemoryRepository.CreateClaimAsync(
                    companionId,
                    new SemanticClaim(
                        Guid.NewGuid(),
                        supersede.Replacement.Subject,
                        supersede.Replacement.Predicate,
                        supersede.Replacement.Value,
                        supersede.Replacement.Confidence,
                        string.IsNullOrWhiteSpace(supersede.Replacement.Scope) ? "global" : supersede.Replacement.Scope,
                        SemanticClaimStatus.Active,
                        null,
                        null,
                        null,
                        now,
                        now),
                    cancellationToken);

                await semanticMemoryRepository.SupersedeAsync(companionId, existing.ClaimId, replacement.ClaimId, cancellationToken);
            }
            appliedClaimsToSupersede += 1;
        }

        foreach (var routine in outcome.ProceduralUpdates)
        {
            if (string.IsNullOrWhiteSpace(routine.Trigger) || routine.Steps.Count == 0)
            {
                skips.Add(new SubconsciousApplySkip("procedural.upsert", routine.Name, "missing_trigger_or_steps", null));
                continue;
            }

            if (!dryRun)
            {
                await proceduralMemoryRepository.UpsertAsync(
                    companionId,
                    new ProceduralRoutine(
                        routine.RoutineId ?? Guid.NewGuid(),
                        routine.Trigger,
                        routine.Name,
                        routine.Steps.ToArray(),
                        [],
                        routine.Outcome,
                        now,
                        now),
                    cancellationToken);
            }
            appliedProcedural += 1;
        }

        foreach (var update in outcome.SelfUpdates)
        {
            if (update.Confidence < 0.75 || string.IsNullOrWhiteSpace(update.Key))
            {
                var reason = string.IsNullOrWhiteSpace(update.Key) ? "missing_key" : "confidence_below_min_0.75";
                skips.Add(new SubconsciousApplySkip("self.update", update.Key, reason, update.Confidence));
                continue;
            }

            if (!dryRun)
            {
                await selfModelRepository.SetPreferenceAsync(companionId, update.Key, update.Value, cancellationToken);
            }
            appliedSelf += 1;
        }

        var report = new SubconsciousApplyReport(
            ProposedClaimsToCreate: outcome.ClaimsToCreate.Count,
            AppliedClaimsToCreate: appliedClaimsToCreate,
            ProposedClaimsToSupersede: outcome.ClaimsToSupersede.Count,
            AppliedClaimsToSupersede: appliedClaimsToSupersede,
            ProposedProceduralUpdates: outcome.ProceduralUpdates.Count,
            AppliedProceduralUpdates: appliedProcedural,
            ProposedSelfUpdates: outcome.SelfUpdates.Count,
            AppliedSelfUpdates: appliedSelf,
            Skipped: skips,
            AnyApplied: appliedClaimsToCreate + appliedClaimsToSupersede + appliedProcedural + appliedSelf > 0);

        logger.LogInformation(
            "Subconscious outcome {Mode}. DebateId={DebateId} SessionId={SessionId} AnyApplied={AnyApplied} Create={CreateApplied}/{CreateProposed} Supersede={SupersedeApplied}/{SupersedeProposed} Procedural={ProceduralApplied}/{ProceduralProposed} Self={SelfApplied}/{SelfProposed} Skipped={Skipped}",
            dryRun ? "previewed" : "applied",
            debateId,
            sessionId,
            report.AnyApplied,
            report.AppliedClaimsToCreate,
            report.ProposedClaimsToCreate,
            report.AppliedClaimsToSupersede,
            report.ProposedClaimsToSupersede,
            report.AppliedProceduralUpdates,
            report.ProposedProceduralUpdates,
            report.AppliedSelfUpdates,
            report.ProposedSelfUpdates,
            report.Skipped.Count);

        return report;
    }
}
