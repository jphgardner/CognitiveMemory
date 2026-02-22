namespace CognitiveMemory.Infrastructure.Scheduling;

public sealed class ScheduledActionOptions
{
    public bool Enabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 2;
    public int BatchSize { get; set; } = 25;
    public int DefaultMaxAttempts { get; set; } = 3;
}
