using System.ComponentModel.DataAnnotations;

namespace CognitiveMemory.Application.Contracts;

public class CreateClaimRequest
{
    public Guid SubjectEntityId { get; set; }

    [Required]
    [MaxLength(128)]
    public string Predicate { get; set; } = string.Empty;

    public Guid? ObjectEntityId { get; set; }

    public string? LiteralValue { get; set; }

    [MaxLength(32)]
    public string ValueType { get; set; } = "String";

    [Range(0.0, 1.0)]
    public double Confidence { get; set; } = 0.5;

    public string Scope { get; set; } = "{}";

    public DateTimeOffset? ValidFrom { get; set; }

    public DateTimeOffset? ValidTo { get; set; }

    [MaxLength(128)]
    public string Hash { get; set; } = string.Empty;

    [MinLength(1)]
    public List<CreateEvidenceRequest> Evidence { get; set; } = [];
}
