namespace CognitiveMemory.Application.AI;

public sealed class SemanticKernelHealthStatus
{
    public required string Provider { get; init; }

    public required string ModelStatus { get; init; }
}
