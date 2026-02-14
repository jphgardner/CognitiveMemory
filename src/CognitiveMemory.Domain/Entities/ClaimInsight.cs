using System.ComponentModel.DataAnnotations;

namespace CognitiveMemory.Domain.Entities;

public sealed class ClaimInsight
{
    [Key]
    public Guid ClaimId { get; set; }

    public string Summary { get; set; } = string.Empty;

    public string KeywordsJson { get; set; } = "[]";

    [MaxLength(64)]
    public string SourceEventRef { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; }
}
