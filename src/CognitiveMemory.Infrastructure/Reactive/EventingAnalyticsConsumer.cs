using System.Diagnostics.Metrics;
using CognitiveMemory.Infrastructure.Events;

namespace CognitiveMemory.Infrastructure.Reactive;

public sealed class EventingAnalyticsConsumer : IOutboxEventConsumer
{
    private static readonly Meter Meter = new("CognitiveMemory.Eventing");
    private static readonly Counter<long> EventCount = Meter.CreateCounter<long>("eventing.events.processed");

    public string ConsumerName => nameof(EventingAnalyticsConsumer);

    public bool CanHandle(string eventType) => true;

    public Task HandleAsync(OutboxEvent @event, CancellationToken cancellationToken = default)
    {
        EventCount.Add(1, new KeyValuePair<string, object?>("event_type", @event.EventType));
        return Task.CompletedTask;
    }
}
