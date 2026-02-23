using CognitiveMemory.Application.Consolidation;
using CognitiveMemory.Infrastructure.Events;
using Microsoft.Extensions.Logging;

namespace CognitiveMemory.Infrastructure.Reactive;

public sealed class ConsolidationOnEpisodicCreatedConsumer(
    IConsolidationService service,
    ILogger<ConsolidationOnEpisodicCreatedConsumer> logger) : IOutboxEventConsumer
{
    public string ConsumerName => nameof(ConsolidationOnEpisodicCreatedConsumer);

    public bool CanHandle(string eventType)
        => string.Equals(eventType, MemoryEventTypes.EpisodicMemoryCreated, StringComparison.Ordinal);

    public async Task HandleAsync(OutboxEvent @event, CancellationToken cancellationToken = default)
    {
        var result = await service.RunOnceAsync(cancellationToken);
        logger.LogInformation(
            "Reactive consolidation run complete. EventId={EventId} Scanned={Scanned} Promoted={Promoted}",
            @event.EventId,
            result.Scanned,
            result.Promoted);
    }
}
