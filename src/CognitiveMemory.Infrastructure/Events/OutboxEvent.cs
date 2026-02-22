namespace CognitiveMemory.Infrastructure.Events;

public sealed record OutboxEvent(
    Guid EventId,
    string EventType,
    string AggregateType,
    string AggregateId,
    DateTimeOffset OccurredAtUtc,
    string PayloadJson,
    string HeadersJson,
    int RetryCount);
