namespace CognitiveMemory.Infrastructure.Persistence.Entities;

public sealed class SemanticClaimEmbeddingEntity
{
    public Guid ClaimId { get; set; }
    public string ModelId { get; set; } = string.Empty;
    public int Dimensions { get; set; }
    public string VectorJson { get; set; } = "[]";
    public string ContentHash { get; set; } = string.Empty;
    public DateTimeOffset EmbeddedAtUtc { get; set; }
}
