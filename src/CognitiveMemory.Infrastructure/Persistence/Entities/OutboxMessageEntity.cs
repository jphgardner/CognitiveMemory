namespace CognitiveMemory.Infrastructure.Persistence.Entities;

public sealed class OutboxMessageEntity
{
    public Guid EventId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string AggregateType { get; set; } = string.Empty;
    public string AggregateId { get; set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
    public string HeadersJson { get; set; } = "{}";
    public string Status { get; set; } = "Pending";
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? LastAttemptedAtUtc { get; set; }
    public DateTimeOffset? PublishedAtUtc { get; set; }
}
