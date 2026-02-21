using CognitiveMemory.Application.Truth;

namespace CognitiveMemory.Api.Endpoints;

public static class TruthMaintenanceEndpoints
{
    public static IEndpointRouteBuilder MapTruthMaintenanceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/truth/run-once",
                async (ITruthMaintenanceService service, CancellationToken cancellationToken) =>
                {
                    var result = await service.RunOnceAsync(cancellationToken);
                    return Results.Ok(result);
                })
            .WithName("RunTruthMaintenanceOnce")
            .WithTags("Truth");

        return endpoints;
    }
}
