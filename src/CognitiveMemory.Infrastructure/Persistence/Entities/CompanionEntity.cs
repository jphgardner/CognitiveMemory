namespace CognitiveMemory.Infrastructure.Persistence.Entities;

public sealed class CompanionEntity
{
    public Guid CompanionId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Tone { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string ModelHint { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string OriginStory { get; set; } = string.Empty;
    public DateTimeOffset? BirthDateUtc { get; set; }
    public string? InitialMemoryText { get; set; }
    public Guid? ActiveCognitiveProfileVersionId { get; set; }
    public string MetadataJson { get; set; } = "{}";
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public bool IsArchived { get; set; }
}
