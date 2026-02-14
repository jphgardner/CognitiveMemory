using CognitiveMemory.Application.AI.Tooling;

namespace CognitiveMemory.Api.Background;

public sealed class ConscienceCalibrationOptions
{
    public const string SectionName = "ConscienceCalibration";

    public bool EnableClaimConfidenceWriteBack { get; init; } = true;

    public double MaxRiskScoreForWriteBack { get; init; } = 0.7;

    public double MinDeltaToWriteBack { get; init; } = 0.05;

    public double MaxStepPerUpdate { get; init; } = 0.2;

    public string[] AllowedDecisions { get; init; } =
    [
        ConsciencePolicy.Approve,
        ConsciencePolicy.Downgrade,
        ConsciencePolicy.Revise
    ];
}
