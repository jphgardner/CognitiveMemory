namespace CognitiveMemory.Application.Reasoning;

public sealed class CognitiveReasoningOptions
{
    public int LookbackHours { get; set; } = 48;
    public int MaxEpisodesScanned { get; set; } = 600;
    public int MaxClaimsScanned { get; set; } = 500;
    public int MinPatternOccurrences { get; set; } = 2;
    public double BaseInferenceConfidence { get; set; } = 0.55;
    public double ConfidenceStepPerOccurrence { get; set; } = 0.08;
    public double MaxInferredConfidence { get; set; } = 0.95;
    public double MinConfidenceDeltaForAdjustment { get; set; } = 0.08;
    public double WeakClaimThreshold { get; set; } = 0.4;
    public bool SuggestProceduralPatterns { get; set; } = true;
}
