using CognitiveMemory.Infrastructure.Events;
using CognitiveMemory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CognitiveMemory.Infrastructure.Background;

public sealed class OutboxDispatcherWorker(
    IServiceScopeFactory scopeFactory,
    EventDrivenOptions options,
    ILogger<OutboxDispatcherWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Outbox dispatcher disabled.");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(1, options.PollIntervalSeconds));
        using var timer = new PeriodicTimer(interval);
        logger.LogInformation("Outbox dispatcher started. Poll={Poll}s Batch={Batch} MaxRetries={MaxRetries}", options.PollIntervalSeconds, options.BatchSize, options.MaxRetries);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await DispatchBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox dispatch cycle failed.");
            }
        }
    }

    private async Task DispatchBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MemoryDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IOutboxPublisher>();

        var rows = await dbContext.OutboxMessages
            .Where(x => x.Status == "Pending" || x.Status == "Failed")
            .OrderBy(x => x.OccurredAtUtc)
            .Take(Math.Clamp(options.BatchSize, 1, 500))
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return;
        }

        foreach (var row in rows)
        {
            var @event = new OutboxEvent(
                row.EventId,
                row.EventType,
                row.AggregateType,
                row.AggregateId,
                row.OccurredAtUtc,
                row.PayloadJson,
                row.HeadersJson,
                row.RetryCount);

            try
            {
                row.Status = "Processing";
                row.LastAttemptedAtUtc = DateTimeOffset.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);

                await publisher.PublishAsync(@event, cancellationToken);

                row.Status = "Published";
                row.PublishedAtUtc = DateTimeOffset.UtcNow;
                row.LastError = null;
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                row.RetryCount += 1;
                row.LastError = ex.Message;
                row.LastAttemptedAtUtc = DateTimeOffset.UtcNow;
                row.Status = row.RetryCount >= Math.Max(1, options.MaxRetries) ? "DeadLetter" : "Failed";
                await dbContext.SaveChangesAsync(cancellationToken);

                logger.LogWarning(
                    ex,
                    "Outbox event publish failed. EventId={EventId} Type={Type} Retry={Retry} Status={Status}",
                    row.EventId,
                    row.EventType,
                    row.RetryCount,
                    row.Status);
            }
        }
    }
}
