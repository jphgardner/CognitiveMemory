namespace CognitiveMemory.Domain.Memory;

public sealed record MemoryEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    MemoryLayer Layer,
    string Summary,
    string SourceReference);
