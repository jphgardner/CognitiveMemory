using CognitiveMemory.Infrastructure.Events;
using CognitiveMemory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CognitiveMemory.Infrastructure.Background;

public sealed class DeadLetterRecoveryWorker(
    IServiceScopeFactory scopeFactory,
    EventDrivenOptions options,
    ILogger<DeadLetterRecoveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled || !options.DeadLetterRecovery.Enabled)
        {
            logger.LogInformation("Dead-letter recovery disabled.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, options.DeadLetterRecovery.IntervalMinutes)));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RecoverAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Dead-letter recovery cycle failed.");
            }
        }
    }

    private async Task RecoverAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MemoryDbContext>();

        var rows = await dbContext.OutboxMessages
            .Where(x => x.Status == "DeadLetter")
            .OrderBy(x => x.OccurredAtUtc)
            .Take(Math.Clamp(options.DeadLetterRecovery.ReplayBatchSize, 1, 500))
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return;
        }

        foreach (var row in rows)
        {
            row.Status = "Pending";
            row.RetryCount = 0;
            row.LastError = null;
            row.LastAttemptedAtUtc = null;
            row.PublishedAtUtc = null;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Dead-letter recovery replayed {Count} event(s).", rows.Count);
    }
}
