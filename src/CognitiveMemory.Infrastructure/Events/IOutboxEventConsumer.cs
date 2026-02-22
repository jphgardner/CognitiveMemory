namespace CognitiveMemory.Infrastructure.Events;

public interface IOutboxEventConsumer
{
    string ConsumerName { get; }
    bool CanHandle(string eventType);
    Task HandleAsync(OutboxEvent @event, CancellationToken cancellationToken = default);
}
