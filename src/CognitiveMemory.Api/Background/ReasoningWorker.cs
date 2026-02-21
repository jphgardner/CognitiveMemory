using CognitiveMemory.Application.Reasoning;

namespace CognitiveMemory.Api.Background;

public sealed class ReasoningWorker(
    IServiceProvider serviceProvider,
    ILogger<ReasoningWorker> logger,
    ReasoningWorkerOptions options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Reasoning worker disabled.");
            return;
        }

        var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(2, options.IntervalMinutes)));
        logger.LogInformation("Reasoning worker started with interval {IntervalMinutes}m.", options.IntervalMinutes);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            using var scope = serviceProvider.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<ICognitiveReasoningService>();
            var result = await service.RunOnceAsync(stoppingToken);

            logger.LogInformation(
                "Reasoning run complete. Episodes={Episodes} Claims={Claims} Inferred={Inferred} Adjusted={Adjusted} Weak={Weak} SuggestedProcedures={Suggested}",
                result.EpisodesScanned,
                result.ClaimsScanned,
                result.InferredClaims,
                result.ConfidenceAdjustments,
                result.WeakClaimsIdentified,
                result.ProceduralSuggestions);
        }
    }
}
