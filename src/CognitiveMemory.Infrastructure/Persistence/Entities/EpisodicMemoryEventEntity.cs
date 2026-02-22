namespace CognitiveMemory.Infrastructure.Persistence.Entities;

public sealed class EpisodicMemoryEventEntity
{
    public Guid EventId { get; set; }
    public Guid CompanionId { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string Who { get; set; } = string.Empty;
    public string What { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
    public string Context { get; set; } = string.Empty;
    public string SourceReference { get; set; } = string.Empty;
}
