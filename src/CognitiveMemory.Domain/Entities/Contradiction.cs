using System.ComponentModel.DataAnnotations;

namespace CognitiveMemory.Domain.Entities;

public class Contradiction
{
    [Key]
    public Guid ContradictionId { get; set; }

    public Guid ClaimAId { get; set; }

    public Guid ClaimBId { get; set; }

    [MaxLength(32)]
    public string Type { get; set; } = "Direct";

    [MaxLength(16)]
    public string Severity { get; set; } = "Medium";

    public DateTimeOffset DetectedAt { get; set; }

    [MaxLength(32)]
    public string Status { get; set; } = "Open";

    public string? ResolutionNotes { get; set; }
}
