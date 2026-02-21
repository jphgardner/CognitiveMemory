using CognitiveMemory.Application.Truth;

namespace CognitiveMemory.Api.Background;

public sealed class TruthMaintenanceWorker(
    IServiceProvider serviceProvider,
    ILogger<TruthMaintenanceWorker> logger,
    TruthMaintenanceWorkerOptions options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Truth maintenance worker disabled.");
            return;
        }

        var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(5, options.IntervalMinutes)));
        logger.LogInformation("Truth maintenance worker started with interval {IntervalMinutes}m.", options.IntervalMinutes);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            using var scope = serviceProvider.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<ITruthMaintenanceService>();
            var result = await service.RunOnceAsync(stoppingToken);

            logger.LogInformation(
                "Truth run complete. Claims={Claims} ConflictClusters={Conflicts} Contradictions={Contradictions} Adjustments={Adjustments} Probabilistic={Probabilistic}",
                result.ClaimsScanned,
                result.ConflictClusters,
                result.ContradictionsRecorded,
                result.ConfidenceAdjustments,
                result.ProbabilisticMarks);
        }
    }
}
