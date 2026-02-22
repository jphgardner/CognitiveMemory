using CognitiveMemory.Application.Reasoning;
using CognitiveMemory.Infrastructure.Events;
using Microsoft.Extensions.Logging;

namespace CognitiveMemory.Infrastructure.Reactive;

public sealed class ReasoningReactiveConsumer(
    ICognitiveReasoningService service,
    ILogger<ReasoningReactiveConsumer> logger) : IOutboxEventConsumer
{
    public string ConsumerName => nameof(ReasoningReactiveConsumer);

    public bool CanHandle(string eventType)
        => eventType is MemoryEventTypes.EpisodicMemoryCreated
            or MemoryEventTypes.SemanticClaimCreated
            or MemoryEventTypes.ProceduralRoutineUpserted;

    public async Task HandleAsync(OutboxEvent @event, CancellationToken cancellationToken = default)
    {
        var result = await service.RunOnceAsync(cancellationToken);
        logger.LogInformation(
            "Reactive reasoning run complete. EventId={EventId} Inferred={Inferred} Adjusted={Adjusted}",
            @event.EventId,
            result.InferredClaims,
            result.ConfidenceAdjustments);
    }
}
