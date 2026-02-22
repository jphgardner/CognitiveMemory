namespace CognitiveMemory.Infrastructure.Persistence.Entities;

public sealed class ClaimEvidenceEntity
{
    public Guid EvidenceId { get; set; }
    public Guid CompanionId { get; set; }
    public Guid ClaimId { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public string SourceReference { get; set; } = string.Empty;
    public string ExcerptOrSummary { get; set; } = string.Empty;
    public double Strength { get; set; }
    public DateTimeOffset CapturedAtUtc { get; set; }
}
