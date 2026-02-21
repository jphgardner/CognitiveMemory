namespace CognitiveMemory.Infrastructure.Persistence.Entities;

public sealed class ConsolidationPromotionEntity
{
    public Guid PromotionId { get; set; }
    public Guid EpisodicEventId { get; set; }
    public Guid? SemanticClaimId { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTimeOffset ProcessedAtUtc { get; set; }
}
