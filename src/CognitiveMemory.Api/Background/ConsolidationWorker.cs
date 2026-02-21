using CognitiveMemory.Application.Consolidation;
using System.Diagnostics.Metrics;

namespace CognitiveMemory.Api.Background;

public sealed class ConsolidationWorker(
    IServiceProvider serviceProvider,
    ILogger<ConsolidationWorker> logger,
    ConsolidationWorkerOptions options) : BackgroundService
{
    private static readonly Meter Meter = new("CognitiveMemory.Consolidation");
    private static readonly Counter<long> Runs = Meter.CreateCounter<long>("consolidation.runs");
    private static readonly Counter<long> Promoted = Meter.CreateCounter<long>("consolidation.promoted");
    private static readonly Counter<long> Errors = Meter.CreateCounter<long>("consolidation.errors");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Consolidation worker is disabled.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, options.IntervalSeconds)));
        logger.LogInformation("Consolidation worker started with interval {IntervalSeconds}s.", options.IntervalSeconds);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var scope = serviceProvider.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IConsolidationService>();

                    var result = await service.RunOnceAsync(stoppingToken);
                    Runs.Add(1);
                    Promoted.Add(result.Promoted);
                    logger.LogInformation(
                        "Consolidation run completed. Scanned={Scanned} Processed={Processed} Promoted={Promoted} Skipped={Skipped}",
                        result.Scanned,
                        result.Processed,
                        result.Promoted,
                        result.Skipped);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Errors.Add(1);
                    logger.LogError(ex, "Consolidation run failed. Worker will continue on next interval.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown path.
        }
    }
}
