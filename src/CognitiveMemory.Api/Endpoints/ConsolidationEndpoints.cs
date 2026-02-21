using CognitiveMemory.Application.Consolidation;

namespace CognitiveMemory.Api.Endpoints;

public static class ConsolidationEndpoints
{
    public static IEndpointRouteBuilder MapConsolidationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/consolidation/run-once",
                async (IConsolidationService consolidationService, CancellationToken cancellationToken) =>
                {
                    var result = await consolidationService.RunOnceAsync(cancellationToken);
                    return Results.Ok(
                        new ConsolidationRunResultDto(
                            result.Scanned,
                            result.Processed,
                            result.Promoted,
                            result.Skipped,
                            result.StartedAtUtc,
                            result.FinishedAtUtc));
                })
            .WithName("RunConsolidationOnce")
            .WithTags("Consolidation");

        return endpoints;
    }
}

public sealed record ConsolidationRunResultDto(
    int Scanned,
    int Processed,
    int Promoted,
    int Skipped,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc);
