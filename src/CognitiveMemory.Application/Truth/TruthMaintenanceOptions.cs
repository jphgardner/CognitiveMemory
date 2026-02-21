namespace CognitiveMemory.Application.Truth;

public sealed class TruthMaintenanceOptions
{
    public int MaxClaimsScanned { get; set; } = 600;
    public double ConflictConfidencePenalty { get; set; } = 0.1;
    public double UncertainThreshold { get; set; } = 0.45;
    public double MinConfidenceDeltaForAdjustment { get; set; } = 0.05;
    public int MaxConflictPairsPerRun { get; set; } = 20;
}
