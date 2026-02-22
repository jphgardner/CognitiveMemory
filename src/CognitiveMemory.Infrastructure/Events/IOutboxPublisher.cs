namespace CognitiveMemory.Infrastructure.Events;

public interface IOutboxPublisher
{
    Task PublishAsync(OutboxEvent @event, CancellationToken cancellationToken = default);
}
