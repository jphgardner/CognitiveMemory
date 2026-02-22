using CognitiveMemory.Infrastructure.Persistence;
using CognitiveMemory.Infrastructure.Subconscious;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CognitiveMemory.Infrastructure.Background;

public sealed class SubconsciousDebateWorker(
    IServiceScopeFactory scopeFactory,
    SubconsciousDebateOptions options,
    ILogger<SubconsciousDebateWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Subconscious debate worker disabled.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, options.PollIntervalSeconds)));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Subconscious debate worker cycle failed.");
            }
        }
    }

    private async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MemoryDbContext>();
        var debateService = scope.ServiceProvider.GetRequiredService<ISubconsciousDebateService>();

        var queued = await dbContext.SubconsciousDebateSessions
            .AsNoTracking()
            .Where(x => x.State == nameof(SubconsciousSessionState.Queued))
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.CreatedAtUtc)
            .Take(Math.Clamp(options.MaxConcurrentDebates, 1, 16))
            .Select(x => x.DebateId)
            .ToArrayAsync(cancellationToken);

        if (queued.Length == 0)
        {
            return;
        }

        foreach (var debateId in queued)
        {
            await debateService.ProcessDebateAsync(debateId, cancellationToken);
        }
    }
}
