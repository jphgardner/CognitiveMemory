using System.Diagnostics.Metrics;
using CognitiveMemory.Infrastructure.Events;
using Microsoft.Extensions.Logging;

namespace CognitiveMemory.Infrastructure.Reactive;

public sealed class EventingSlaConsumer(
    EventDrivenOptions options,
    ILogger<EventingSlaConsumer> logger) : IOutboxEventConsumer
{
    private static readonly Meter Meter = new("CognitiveMemory.Eventing");
    private static readonly Histogram<double> LagSeconds = Meter.CreateHistogram<double>("eventing.lag.seconds");
    private static readonly Counter<long> SlaBreaches = Meter.CreateCounter<long>("eventing.sla.breaches");

    public string ConsumerName => nameof(EventingSlaConsumer);

    public bool CanHandle(string eventType) => true;

    public Task HandleAsync(OutboxEvent @event, CancellationToken cancellationToken = default)
    {
        var lag = Math.Max(0, (DateTimeOffset.UtcNow - @event.OccurredAtUtc).TotalSeconds);
        LagSeconds.Record(lag, new KeyValuePair<string, object?>("event_type", @event.EventType));

        var warningThreshold = Math.Max(1, options.SlaWarningLagSeconds);
        var errorThreshold = Math.Max(warningThreshold, options.SlaErrorLagSeconds);
        if (options.SlaByEventType.TryGetValue(@event.EventType, out var @override))
        {
            if (@override.WarningLagSeconds.HasValue)
            {
                warningThreshold = Math.Max(1, @override.WarningLagSeconds.Value);
            }

            if (@override.ErrorLagSeconds.HasValue)
            {
                errorThreshold = Math.Max(1, @override.ErrorLagSeconds.Value);
            }

            errorThreshold = Math.Max(errorThreshold, warningThreshold);
        }

        if (lag >= errorThreshold)
        {
            SlaBreaches.Add(1, new KeyValuePair<string, object?>("severity", "error"));
            logger.LogError(
                "Eventing SLA breach (error). EventId={EventId} Type={Type} LagSeconds={Lag}",
                @event.EventId,
                @event.EventType,
                lag);
        }
        else if (lag >= warningThreshold)
        {
            SlaBreaches.Add(1, new KeyValuePair<string, object?>("severity", "warning"));
            logger.LogWarning(
                "Eventing SLA breach (warning). EventId={EventId} Type={Type} LagSeconds={Lag}",
                @event.EventId,
                @event.EventType,
                lag);
        }

        return Task.CompletedTask;
    }
}
