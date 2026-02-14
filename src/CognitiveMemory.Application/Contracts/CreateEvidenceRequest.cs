using System.ComponentModel.DataAnnotations;

namespace CognitiveMemory.Application.Contracts;

public class CreateEvidenceRequest
{
    [MaxLength(32)]
    public string SourceType { get; set; } = "Other";

    [Required]
    [MaxLength(256)]
    public string SourceRef { get; set; } = string.Empty;

    [Required]
    public string ExcerptOrSummary { get; set; } = string.Empty;

    [Range(0.0, 1.0)]
    public double Strength { get; set; } = 0.5;

    public DateTimeOffset? CapturedAt { get; set; }

    public string Metadata { get; set; } = "{}";
}
