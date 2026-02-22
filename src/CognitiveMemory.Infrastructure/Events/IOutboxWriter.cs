namespace CognitiveMemory.Infrastructure.Events;

public interface IOutboxWriter
{
    void Enqueue(string eventType, string aggregateType, string aggregateId, object payload, object? headers = null);
}
