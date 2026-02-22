using System.Text.Json;

namespace CognitiveMemory.Infrastructure.Subconscious;

public sealed class SubconsciousOutcomeValidator(SubconsciousDebateOptions options) : ISubconsciousOutcomeValidator
{
    public SubconsciousValidationResult Validate(string outcomeJson)
    {
        if (string.IsNullOrWhiteSpace(outcomeJson))
        {
            return new SubconsciousValidationResult(false, false, "Rejected", "Outcome JSON was empty.", null);
        }

        SubconsciousDebateOutcome? outcome;
        try
        {
            outcome = JsonSerializer.Deserialize<SubconsciousDebateOutcome>(outcomeJson, SubconsciousDebateOutcome.JsonOptions);
        }
        catch (Exception ex)
        {
            return new SubconsciousValidationResult(false, false, "Rejected", $"Outcome JSON parse failed: {ex.Message}", null);
        }

        if (outcome is null)
        {
            return new SubconsciousValidationResult(false, false, "Rejected", "Outcome JSON resolved to null.", null);
        }

        outcome = NormalizeOutcome(outcome);

        if (outcome.FinalConfidence is < 0 or > 1)
        {
            return new SubconsciousValidationResult(false, false, "Rejected", "finalConfidence must be within [0,1].", null);
        }

        foreach (var claim in outcome.ClaimsToCreate)
        {
            if (string.IsNullOrWhiteSpace(claim.Subject) || string.IsNullOrWhiteSpace(claim.Predicate) || string.IsNullOrWhiteSpace(claim.Value))
            {
                return new SubconsciousValidationResult(false, false, "Rejected", "claimsToCreate entries must have subject/predicate/value.", null);
            }

            if (claim.Confidence is < 0 or > 1)
            {
                return new SubconsciousValidationResult(false, false, "Rejected", "claimsToCreate confidence must be within [0,1].", null);
            }
        }

        foreach (var update in outcome.SelfUpdates)
        {
            if (update.Confidence is < 0 or > 1)
            {
                return new SubconsciousValidationResult(false, false, "Rejected", "selfUpdates confidence must be within [0,1].", null);
            }

            if (options.RequireHumanApprovalForProtectedIdentity
                && options.ProtectedIdentityKeys.Any(x => x.Equals(update.Key, StringComparison.OrdinalIgnoreCase)))
            {
                if (IsProtectedIdentityDowngrade(update))
                {
                    continue;
                }

                return new SubconsciousValidationResult(true, true, "NeedsUserConfirmation", null, outcome);
            }
        }

        if (outcome.RequiresUserInput)
        {
            return new SubconsciousValidationResult(true, true, "NeedsUserConfirmation", null, outcome);
        }

        return new SubconsciousValidationResult(true, false, "Valid", null, outcome);
    }

    private SubconsciousDebateOutcome NormalizeOutcome(SubconsciousDebateOutcome outcome)
    {
        if (!outcome.RequiresUserInput)
        {
            return outcome;
        }

        // Do not block on model-level caution when the proposed changes are pure certainty downgrades.
        var hasBlockingConfirmation = outcome.SelfUpdates.Any(
            x => x.RequiresConfirmation && !IsProtectedIdentityDowngrade(x));
        if (hasBlockingConfirmation)
        {
            return outcome;
        }

        var allProtectedUpdatesAreDowngrades = outcome.SelfUpdates
            .Where(x => options.ProtectedIdentityKeys.Any(k => k.Equals(x.Key, StringComparison.OrdinalIgnoreCase)))
            .All(IsProtectedIdentityDowngrade);
        if (!allProtectedUpdatesAreDowngrades)
        {
            return outcome;
        }

        var hasAnyWrites = outcome.ClaimsToCreate.Count > 0
                           || outcome.ClaimsToSupersede.Count > 0
                           || outcome.ProceduralUpdates.Count > 0
                           || outcome.SelfUpdates.Count > 0;
        if (!hasAnyWrites)
        {
            return outcome;
        }

        return outcome with
        {
            RequiresUserInput = false,
            UserQuestion = null
        };
    }

    private bool IsProtectedIdentityDowngrade(SubconsciousDebateSelfUpdate update)
    {
        if (!options.AllowAutomaticProtectedIdentityDowngrade)
        {
            return false;
        }

        var value = update.Value?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return options.ProtectedIdentityDowngradeMarkers.Any(
            marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }
}
