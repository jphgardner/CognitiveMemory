namespace CognitiveMemory.Application.Contracts;

public class MemoryHealthResponse
{
    public string Database { get; init; } = "unknown";

    public string Cache { get; init; } = "unknown";

    public double CacheLatencyMs { get; init; }

    public string Model { get; init; } = "unknown";

    public string ModelProvider { get; init; } = "unknown";
}
