using CognitiveMemory.Application.Reasoning;

namespace CognitiveMemory.Api.Endpoints;

public static class CognitiveReasoningEndpoints
{
    public static IEndpointRouteBuilder MapCognitiveReasoningEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/reasoning/run-once",
                async (ICognitiveReasoningService service, CancellationToken cancellationToken) =>
                {
                    var result = await service.RunOnceAsync(cancellationToken);
                    return Results.Ok(result);
                })
            .WithName("RunCognitiveReasoningOnce")
            .WithTags("Reasoning");

        return endpoints;
    }
}
