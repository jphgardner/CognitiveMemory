using CognitiveMemory.Application.Identity;

namespace CognitiveMemory.Api.Background;

public sealed class IdentityEvolutionWorker(
    IServiceProvider serviceProvider,
    ILogger<IdentityEvolutionWorker> logger,
    IdentityEvolutionWorkerOptions options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Identity evolution worker disabled.");
            return;
        }

        var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(5, options.IntervalMinutes)));
        logger.LogInformation("Identity evolution worker started with interval {IntervalMinutes}m.", options.IntervalMinutes);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IIdentityEvolutionService>();
                var result = await service.RunOnceAsync(stoppingToken);

                logger.LogInformation(
                    "Identity evolution run complete. Episodes={Episodes} Claims={Claims} Routines={Routines} PreferencesUpdated={Updated}",
                    result.EpisodesScanned,
                    result.ClaimsScanned,
                    result.ProceduresScanned,
                    result.PreferencesUpdated);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Identity evolution worker cycle failed.");
            }
        }
    }
}
