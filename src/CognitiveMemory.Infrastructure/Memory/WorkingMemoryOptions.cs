namespace CognitiveMemory.Infrastructure.Memory;

public sealed class WorkingMemoryOptions
{
    public int TtlSeconds { get; set; } = 1800;
    public string KeyPrefix { get; set; } = "working-memory:session:";
}
