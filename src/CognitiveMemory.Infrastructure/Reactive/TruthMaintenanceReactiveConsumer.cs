using CognitiveMemory.Application.Truth;
using CognitiveMemory.Infrastructure.Events;
using Microsoft.Extensions.Logging;

namespace CognitiveMemory.Infrastructure.Reactive;

public sealed class TruthMaintenanceReactiveConsumer(
    ITruthMaintenanceService service,
    ILogger<TruthMaintenanceReactiveConsumer> logger) : IOutboxEventConsumer
{
    public string ConsumerName => nameof(TruthMaintenanceReactiveConsumer);

    public bool CanHandle(string eventType)
        => eventType is MemoryEventTypes.SemanticClaimCreated
            or MemoryEventTypes.SemanticClaimSuperseded
            or MemoryEventTypes.SemanticEvidenceAdded
            or MemoryEventTypes.SemanticContradictionAdded;

    public async Task HandleAsync(OutboxEvent @event, CancellationToken cancellationToken = default)
    {
        var result = await service.RunOnceAsync(cancellationToken);
        logger.LogInformation(
            "Reactive truth maintenance complete. EventId={EventId} Contradictions={Contradictions} Adjustments={Adjustments}",
            @event.EventId,
            result.ContradictionsRecorded,
            result.ConfidenceAdjustments);
    }
}
