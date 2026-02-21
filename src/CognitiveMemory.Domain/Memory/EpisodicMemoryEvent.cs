namespace CognitiveMemory.Domain.Memory;

public sealed record EpisodicMemoryEvent(
    Guid EventId,
    string SessionId,
    string Who,
    string What,
    DateTimeOffset OccurredAt,
    string Context,
    string SourceReference);
