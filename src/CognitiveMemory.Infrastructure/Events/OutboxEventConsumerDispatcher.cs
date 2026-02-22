using CognitiveMemory.Infrastructure.Persistence;
using CognitiveMemory.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CognitiveMemory.Infrastructure.Events;

public sealed class OutboxEventConsumerDispatcher(
    IServiceScopeFactory scopeFactory,
    ILogger<OutboxEventConsumerDispatcher> logger)
{
    public async Task DispatchAsync(OutboxEvent @event, CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var consumers = scope.ServiceProvider.GetServices<IOutboxEventConsumer>().ToArray();
        if (consumers.Length == 0)
        {
            return;
        }

        var dbContext = scope.ServiceProvider.GetRequiredService<MemoryDbContext>();
        foreach (var consumer in consumers.Where(x => x.CanHandle(@event.EventType)))
        {
            var alreadyProcessed = await dbContext.EventConsumerCheckpoints
                .AsNoTracking()
                .AnyAsync(
                    x => x.ConsumerName == consumer.ConsumerName && x.EventId == @event.EventId,
                    cancellationToken);

            if (alreadyProcessed)
            {
                continue;
            }

            try
            {
                await consumer.HandleAsync(@event, cancellationToken);
                dbContext.EventConsumerCheckpoints.Add(
                    new EventConsumerCheckpointEntity
                    {
                        ConsumerName = consumer.ConsumerName,
                        EventId = @event.EventId,
                        ProcessedAtUtc = DateTimeOffset.UtcNow
                    });

                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Outbox consumer failed. Consumer={Consumer} EventType={EventType} EventId={EventId}",
                    consumer.ConsumerName,
                    @event.EventType,
                    @event.EventId);
                throw;
            }
        }
    }
}
