namespace CognitiveMemory.Application.AI.Tooling;

public static class ConscienceReasonCodes
{
    public const string InsufficientEvidence = "InsufficientEvidence";
    public const string SevereContradiction = "SevereContradiction";
    public const string WeakEvidenceSupport = "WeakEvidenceSupport";
    public const string CloseTopScores = "CloseTopScores";
    public const string TopClaimHasSevereContradiction = "TopClaimHasSevereContradiction";
    public const string ContradictionAnalyst = "ContradictionAnalyst";
    public const string Calibrator = "Calibrator";
    public const string OpenContradictionsPresent = "OpenContradictionsPresent";
    public const string LlmConscience = "LlmConscience";
    public const string HeuristicFallback = "HeuristicFallback";

    public static IReadOnlyDictionary<string, string> Descriptions { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [InsufficientEvidence] = "Evidence coverage is insufficient to support a defensible answer.",
        [SevereContradiction] = "At least one severe contradiction is unresolved.",
        [WeakEvidenceSupport] = "Evidence exists but aggregate support remains weak.",
        [CloseTopScores] = "Top retrieval candidates have close scores, reducing certainty.",
        [TopClaimHasSevereContradiction] = "Highest-ranked claim has a severe contradiction.",
        [ContradictionAnalyst] = "Contradiction analyst role contributed to this decision.",
        [Calibrator] = "Calibration role contributed confidence guidance.",
        [OpenContradictionsPresent] = "Open contradictions exist for this claim.",
        [LlmConscience] = "Decision produced by the LLM conscience analyzer.",
        [HeuristicFallback] = "Deterministic fallback path was used."
    };
}
