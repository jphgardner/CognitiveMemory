using System.ComponentModel.DataAnnotations;

namespace CognitiveMemory.Domain.Entities;

public sealed class ClaimCalibration
{
    [Key]
    public Guid CalibrationId { get; set; }

    public Guid ClaimId { get; set; }

    public double RecommendedConfidence { get; set; }

    [MaxLength(64)]
    public string SourceEventRef { get; set; } = string.Empty;

    public string ReasonCodesJson { get; set; } = "[]";

    public DateTimeOffset CreatedAt { get; set; }
}
