using CognitiveMemory.Application.Procedural;

namespace CognitiveMemory.Api.Endpoints;

public static class ProceduralMemoryEndpoints
{
    public static IEndpointRouteBuilder MapProceduralMemoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/procedural/routines",
                async (UpsertRoutineDto request, IProceduralMemoryService service, CancellationToken cancellationToken) =>
                {
                    var created = await service.UpsertAsync(
                        new UpsertProceduralRoutineRequest(
                            request.RoutineId,
                            request.Trigger,
                            request.Name,
                            request.Steps,
                            request.Checkpoints,
                            request.Outcome),
                        cancellationToken);

                    return Results.Ok(created);
                })
            .WithName("UpsertProceduralRoutine")
            .WithTags("Procedural");

        endpoints.MapGet(
                "/api/procedural/routines",
                async (string trigger, int? take, IProceduralMemoryService service, CancellationToken cancellationToken) =>
                {
                    var routines = await service.QueryByTriggerAsync(trigger, take ?? 20, cancellationToken);
                    return Results.Ok(routines);
                })
            .WithName("QueryProceduralRoutines")
            .WithTags("Procedural");

        return endpoints;
    }
}

public sealed record UpsertRoutineDto(
    Guid? RoutineId,
    string Trigger,
    string Name,
    IReadOnlyList<string> Steps,
    IReadOnlyList<string> Checkpoints,
    string Outcome);
