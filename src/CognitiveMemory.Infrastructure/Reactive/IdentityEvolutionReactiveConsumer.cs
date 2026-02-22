using CognitiveMemory.Application.Identity;
using CognitiveMemory.Infrastructure.Events;
using Microsoft.Extensions.Logging;

namespace CognitiveMemory.Infrastructure.Reactive;

public sealed class IdentityEvolutionReactiveConsumer(
    IIdentityEvolutionService service,
    ILogger<IdentityEvolutionReactiveConsumer> logger) : IOutboxEventConsumer
{
    public string ConsumerName => nameof(IdentityEvolutionReactiveConsumer);

    public bool CanHandle(string eventType)
        => eventType is MemoryEventTypes.EpisodicMemoryCreated
            or MemoryEventTypes.SemanticClaimCreated
            or MemoryEventTypes.ProceduralRoutineUpserted;

    public async Task HandleAsync(OutboxEvent @event, CancellationToken cancellationToken = default)
    {
        var result = await service.RunOnceAsync(cancellationToken);
        logger.LogInformation(
            "Reactive identity evolution complete. EventId={EventId} Updated={Updated}",
            @event.EventId,
            result.PreferencesUpdated);
    }
}
