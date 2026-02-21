using CognitiveMemory.Application.Identity;

namespace CognitiveMemory.Api.Endpoints;

public static class IdentityEvolutionEndpoints
{
    public static IEndpointRouteBuilder MapIdentityEvolutionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/identity/run-once",
                async (IIdentityEvolutionService service, CancellationToken cancellationToken) =>
                {
                    var result = await service.RunOnceAsync(cancellationToken);
                    return Results.Ok(result);
                })
            .WithName("RunIdentityEvolutionOnce")
            .WithTags("Identity");

        return endpoints;
    }
}
