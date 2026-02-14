using System.ComponentModel.DataAnnotations;

namespace CognitiveMemory.Domain.Entities;

public class Evidence
{
    [Key]
    public Guid EvidenceId { get; set; }

    public Guid ClaimId { get; set; }

    [MaxLength(32)]
    public string SourceType { get; set; } = "Other";

    [MaxLength(256)]
    public string SourceRef { get; set; } = string.Empty;

    public string ExcerptOrSummary { get; set; } = string.Empty;

    public double Strength { get; set; }

    public DateTimeOffset CapturedAt { get; set; }

    public string Metadata { get; set; } = "{}";

    public Claim Claim { get; set; } = null!;
}
