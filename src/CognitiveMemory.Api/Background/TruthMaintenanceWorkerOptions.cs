namespace CognitiveMemory.Api.Background;

public sealed class TruthMaintenanceWorkerOptions
{
    public bool Enabled { get; set; } = true;
    public int IntervalMinutes { get; set; } = 45;
}
