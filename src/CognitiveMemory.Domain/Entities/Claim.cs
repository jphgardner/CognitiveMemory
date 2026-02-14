using System.ComponentModel.DataAnnotations;

namespace CognitiveMemory.Domain.Entities;

public class Claim
{
    [Key]
    public Guid ClaimId { get; set; }

    public Guid SubjectEntityId { get; set; }

    [MaxLength(128)]
    public string Predicate { get; set; } = string.Empty;

    public Guid? ObjectEntityId { get; set; }

    public string? LiteralValue { get; set; }

    [MaxLength(32)]
    public string ValueType { get; set; } = "String";

    public double Confidence { get; set; }

    public ClaimStatus Status { get; set; }

    public string Scope { get; set; } = "{}";

    public DateTimeOffset? ValidFrom { get; set; }

    public DateTimeOffset? ValidTo { get; set; }

    public DateTimeOffset? LastReinforcedAt { get; set; }

    [MaxLength(128)]
    public string Hash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<Evidence> Evidence { get; set; } = [];
}
