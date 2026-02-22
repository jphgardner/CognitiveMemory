namespace CognitiveMemory.Infrastructure.Persistence.Entities;

public sealed class ConflictEscalationAlertEntity
{
    public Guid AlertId { get; set; }
    public Guid CompanionId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Predicate { get; set; } = string.Empty;
    public string ValuesJson { get; set; } = "[]";
    public int ContradictionCount { get; set; }
    public string Status { get; set; } = "Open";
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset LastSeenAtUtc { get; set; }
}
