using CognitiveMemory.Infrastructure.Events;
using CognitiveMemory.Application.Cognitive;

namespace CognitiveMemory.Infrastructure.Subconscious;

public sealed class SubconsciousGroupChatManager(SubconsciousDebateOptions options)
{
    public string SelectNextAgent(
        IReadOnlyList<SubconsciousDebateTurnSignal> turns,
        bool unresolvedConflict,
        string triggerEventType,
        string? lastRole,
        int maxTurns,
        CompanionCognitiveProfileDocument? profile = null)
    {
        if (turns.Count == 0)
        {
            return "curator";
        }

        var nearEnd = turns.Count >= Math.Max(2, maxTurns - 1);
        if (nearEnd)
        {
            return "synthesizer";
        }

        var hasSkeptic = turns.Any(x => x.Role == "skeptic");
        var hasHistorian = turns.Any(x => x.Role == "historian");
        var hasStrategist = turns.Any(x => x.Role == "strategist");

        if (!hasSkeptic && triggerEventType is MemoryEventTypes.SemanticContradictionAdded or MemoryEventTypes.SemanticEvidenceAdded)
        {
            return "skeptic";
        }

        if (!hasHistorian)
        {
            return "historian";
        }

        if (unresolvedConflict && !string.Equals(lastRole, "skeptic", StringComparison.OrdinalIgnoreCase))
        {
            return "skeptic";
        }

        if (!hasStrategist || string.Equals(lastRole, "historian", StringComparison.OrdinalIgnoreCase))
        {
            return "strategist";
        }

        if (ShouldTerminate(turns, false, profile?.Reflection.Debate.ConvergenceDeltaMin))
        {
            return "synthesizer";
        }

        if (string.Equals(lastRole, "strategist", StringComparison.OrdinalIgnoreCase))
        {
            return "historian";
        }

        if (string.Equals(lastRole, "skeptic", StringComparison.OrdinalIgnoreCase))
        {
            return "strategist";
        }

        return "historian";
    }

    public bool ShouldTerminate(IReadOnlyList<SubconsciousDebateTurnSignal> turns, bool isSynthesizerTurn, double? convergenceDeltaMin = null)
    {
        if (isSynthesizerTurn)
        {
            return true;
        }

        if (turns.Count < 4)
        {
            return false;
        }

        var last = turns.TakeLast(4).ToArray();
        var distinctMessages = last
            .Select(x => Normalize(x.Message))
            .Distinct(StringComparer.Ordinal)
            .Count();
        if (distinctMessages <= 2)
        {
            return true;
        }

        var confidences = last
            .Select(x => x.Confidence)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .ToArray();
        if (confidences.Length < 2)
        {
            return false;
        }

        var delta = Math.Abs(confidences[^1] - confidences[0]);
        var threshold = Math.Max(0.001, convergenceDeltaMin ?? options.ConvergenceDeltaMin);
        return delta <= threshold;
    }

    public bool ShouldRequestUserInput(SubconsciousDebateOutcome outcome)
    {
        if (outcome.RequiresUserInput)
        {
            var hasBlockingConfirmation = outcome.SelfUpdates.Any(x => x.RequiresConfirmation && !IsProtectedIdentityDowngrade(x));
            if (hasBlockingConfirmation)
            {
                return true;
            }
        }

        if (!options.RequireHumanApprovalForProtectedIdentity)
        {
            return false;
        }

        return outcome.SelfUpdates.Any(
            x => options.ProtectedIdentityKeys.Any(k => k.Equals(x.Key, StringComparison.OrdinalIgnoreCase))
                 && !IsProtectedIdentityDowngrade(x));
    }

    public string FilterResults(string value)
        => value.Trim();

    private static string Normalize(string? value)
    {
        var text = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length <= 180 ? text : text[..180];
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

public sealed record SubconsciousDebateTurnSignal(int TurnNumber, string Role, string Message, double? Confidence);
