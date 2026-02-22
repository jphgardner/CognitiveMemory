using System.Text.Json;
using CognitiveMemory.Infrastructure.Persistence;
using CognitiveMemory.Infrastructure.Persistence.Entities;

namespace CognitiveMemory.Infrastructure.Events;

public sealed class OutboxWriter(MemoryDbContext dbContext) : IOutboxWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Enqueue(string eventType, string aggregateType, string aggregateId, object payload, object? headers = null)
    {
        var now = DateTimeOffset.UtcNow;
        dbContext.OutboxMessages.Add(
            new OutboxMessageEntity
            {
                EventId = Guid.NewGuid(),
                EventType = eventType,
                AggregateType = aggregateType,
                AggregateId = aggregateId,
                OccurredAtUtc = now,
                PayloadJson = JsonSerializer.Serialize(payload, JsonOptions),
                HeadersJson = JsonSerializer.Serialize(headers ?? new { }, JsonOptions),
                Status = "Pending",
                RetryCount = 0
            });
    }
}
