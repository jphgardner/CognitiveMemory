namespace CognitiveMemory.Api.Background;

public sealed class ConscienceOutboxWorkerOptions
{
    public const string SectionName = "ConscienceOutboxWorker";

    public int PollIntervalSeconds { get; init; } = 3;

    public int BatchSize { get; init; } = 16;

    public int LeaseSeconds { get; init; } = 30;

    public int RetryDelaySeconds { get; init; } = 15;
}
