namespace CognitiveMemory.Infrastructure.Persistence.Entities;

public sealed class SubconsciousDebateSessionEntity
{
    public Guid DebateId { get; set; }
    public Guid CompanionId { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string TopicKey { get; set; } = string.Empty;
    public Guid? TriggerEventId { get; set; }
    public string TriggerEventType { get; set; } = string.Empty;
    public string TriggerPayloadJson { get; set; } = "{}";
    public string State { get; set; } = "Queued";
    public int Priority { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
