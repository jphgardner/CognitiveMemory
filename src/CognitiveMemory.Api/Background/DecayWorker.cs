using CognitiveMemory.Application.Semantic;

namespace CognitiveMemory.Api.Background;

public sealed class DecayWorker(
    IServiceProvider serviceProvider,
    ILogger<DecayWorker> logger,
    DecayWorkerOptions options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Decay worker disabled.");
            return;
        }

        var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(5, options.IntervalMinutes)));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<ISemanticMemoryService>();
                var affected = await service.RunDecayAsync(options.StaleDays, options.DecayStep, options.MinConfidence, stoppingToken);
                logger.LogInformation("Decay worker run complete. Affected={Affected}", affected);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Decay worker cycle failed.");
            }
        }
    }
}
