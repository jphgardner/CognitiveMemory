using System.ComponentModel.DataAnnotations;

namespace CognitiveMemory.Domain.Entities;

public sealed class PolicyDecision
{
    [Key]
    public Guid DecisionId { get; set; }

    [MaxLength(64)]
    public string SourceType { get; set; } = string.Empty;

    [MaxLength(256)]
    public string SourceRef { get; set; } = string.Empty;

    [MaxLength(32)]
    public string Decision { get; set; } = string.Empty;

    public double RiskScore { get; set; }

    [MaxLength(64)]
    public string PolicyVersion { get; set; } = string.Empty;

    public string ReasonCodesJson { get; set; } = "[]";

    public string MetadataJson { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }
}
