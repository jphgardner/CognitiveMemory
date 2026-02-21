namespace CognitiveMemory.Api.Background;

public sealed class ReasoningWorkerOptions
{
    public bool Enabled { get; set; } = true;
    public int IntervalMinutes { get; set; } = 20;
}
