namespace CognitiveMemory.Infrastructure.Persistence.Entities;

public sealed class EventConsumerCheckpointEntity
{
    public string ConsumerName { get; set; } = string.Empty;
    public Guid EventId { get; set; }
    public DateTimeOffset ProcessedAtUtc { get; set; }
}
