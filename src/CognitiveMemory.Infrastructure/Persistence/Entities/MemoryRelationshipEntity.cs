namespace CognitiveMemory.Infrastructure.Persistence.Entities;

public sealed class MemoryRelationshipEntity
{
    public Guid RelationshipId { get; set; }
    public Guid CompanionId { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string FromType { get; set; } = string.Empty;
    public string FromId { get; set; } = string.Empty;
    public string ToType { get; set; } = string.Empty;
    public string ToId { get; set; } = string.Empty;
    public string RelationshipType { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public double Strength { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? ValidFromUtc { get; set; }
    public DateTimeOffset? ValidToUtc { get; set; }
    public string? MetadataJson { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
