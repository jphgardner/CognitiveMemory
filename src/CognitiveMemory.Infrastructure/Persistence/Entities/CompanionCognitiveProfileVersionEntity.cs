namespace CognitiveMemory.Infrastructure.Persistence.Entities;

public sealed class CompanionCognitiveProfileVersionEntity
{
    public Guid ProfileVersionId { get; set; }
    public Guid CompanionId { get; set; }
    public int VersionNumber { get; set; }
    public string SchemaVersion { get; set; } = "1.0.0";
    public string ProfileJson { get; set; } = "{}";
    public string CompiledRuntimeJson { get; set; } = "{}";
    public string ProfileHash { get; set; } = string.Empty;
    public string ValidationStatus { get; set; } = "Draft";
    public string CreatedByUserId { get; set; } = string.Empty;
    public string? ChangeSummary { get; set; }
    public string? ChangeReason { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
