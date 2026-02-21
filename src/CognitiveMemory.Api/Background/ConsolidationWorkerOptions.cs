namespace CognitiveMemory.Api.Background;

public sealed class ConsolidationWorkerOptions
{
    public bool Enabled { get; set; } = true;
    public int IntervalSeconds { get; set; } = 20;
}
