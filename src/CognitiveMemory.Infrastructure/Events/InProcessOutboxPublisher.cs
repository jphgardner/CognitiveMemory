namespace CognitiveMemory.Infrastructure.Events;

public sealed class InProcessOutboxPublisher(
    OutboxEventConsumerDispatcher dispatcher) : IOutboxPublisher
{
    public async Task PublishAsync(OutboxEvent @event, CancellationToken cancellationToken = default)
    {
        await dispatcher.DispatchAsync(@event, cancellationToken);
    }
}
