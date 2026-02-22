namespace CognitiveMemory.Infrastructure.Persistence.Entities;

public sealed class ScheduledActionEntity
{
    public Guid ActionId { get; set; }
    public Guid CompanionId { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public string InputJson { get; set; } = "{}";
    public DateTimeOffset RunAtUtc { get; set; }
    public string Status { get; set; } = ScheduledActionStatus.Pending;
    public int Attempts { get; set; }
    public int MaxAttempts { get; set; } = 3;
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
}

public static class ScheduledActionStatus
{
    public const string Pending = "Pending";
    public const string Running = "Running";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
    public const string Canceled = "Canceled";
}
