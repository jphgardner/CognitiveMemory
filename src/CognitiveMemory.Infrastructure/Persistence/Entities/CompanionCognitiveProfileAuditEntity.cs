namespace CognitiveMemory.Infrastructure.Persistence.Entities;

public sealed class CompanionCognitiveProfileAuditEntity
{
    public Guid AuditId { get; set; }
    public Guid CompanionId { get; set; }
    public string ActorUserId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public Guid? FromProfileVersionId { get; set; }
    public Guid? ToProfileVersionId { get; set; }
    public string DiffJson { get; set; } = "{}";
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
