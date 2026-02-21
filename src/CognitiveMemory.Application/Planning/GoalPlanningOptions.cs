namespace CognitiveMemory.Application.Planning;

public sealed class GoalPlanningOptions
{
    public int DefaultLookbackDays { get; set; } = 30;
    public int MaxEpisodesScanned { get; set; } = 500;
    public int MaxSupportingSignals { get; set; } = 12;
    public bool PersistPlansToEpisodicMemory { get; set; } = true;
    public bool AutoUpdateProceduralMemoryOnSuccess { get; set; } = true;
}
