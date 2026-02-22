namespace CognitiveMemory.Infrastructure.Persistence.Entities;

public sealed class CompanionCognitiveProfileEntity
{
    public Guid CompanionId { get; set; }
    public Guid ActiveProfileVersionId { get; set; }
    public Guid? StagedProfileVersionId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string UpdatedByUserId { get; set; } = string.Empty;
}
