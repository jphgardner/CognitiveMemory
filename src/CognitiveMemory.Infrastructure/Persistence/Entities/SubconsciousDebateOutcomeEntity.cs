namespace CognitiveMemory.Infrastructure.Persistence.Entities;

public sealed class SubconsciousDebateOutcomeEntity
{
    public Guid DebateId { get; set; }
    public Guid CompanionId { get; set; }
    public string OutcomeJson { get; set; } = "{}";
    public string OutcomeHash { get; set; } = string.Empty;
    public string ValidationStatus { get; set; } = "Valid";
    public string ApplyStatus { get; set; } = "Pending";
    public string? ApplyError { get; set; }
    public DateTimeOffset? AppliedAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
