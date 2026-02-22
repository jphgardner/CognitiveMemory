using CognitiveMemory.Application.Abstractions;
using CognitiveMemory.Api.Security;
using CognitiveMemory.Domain.Memory;
using CognitiveMemory.Infrastructure.Persistence;

namespace CognitiveMemory.Api.Endpoints;

public static class ProceduralMemoryEndpoints
{
    public static IEndpointRouteBuilder MapProceduralMemoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/procedural").WithTags("Procedural").RequireAuthorization();

        group.MapPost(
                "/routines",
                async (HttpContext httpContext, UpsertRoutineDto request, IProceduralMemoryRepository repository, MemoryDbContext dbContext, CompanionOwnershipService ownershipService, CancellationToken cancellationToken) =>
                {
                    var companion = await ownershipService.ResolveOwnedCompanionAsync(httpContext.User, request.CompanionId, dbContext, cancellationToken);
                    if (companion is null)
                    {
                        return Results.NotFound();
                    }

                    var now = DateTimeOffset.UtcNow;
                    var created = await repository.UpsertAsync(
                        companion.CompanionId,
                        new ProceduralRoutine(
                            request.RoutineId ?? Guid.NewGuid(),
                            request.Trigger,
                            request.Name,
                            request.Steps,
                            request.Checkpoints,
                            request.Outcome,
                            now,
                            now),
                        cancellationToken);

                    return Results.Ok(created);
                })
            .WithName("UpsertProceduralRoutine")
            .WithTags("Procedural");

        group.MapGet(
                "/routines",
                async (HttpContext httpContext, Guid companionId, string trigger, int? take, IProceduralMemoryRepository repository, MemoryDbContext dbContext, CompanionOwnershipService ownershipService, CancellationToken cancellationToken) =>
                {
                    var companion = await ownershipService.ResolveOwnedCompanionAsync(httpContext.User, companionId, dbContext, cancellationToken);
                    if (companion is null)
                    {
                        return Results.NotFound();
                    }

                    var routines = await repository.QueryByTriggerAsync(companion.CompanionId, trigger, take ?? 20, cancellationToken);
                    return Results.Ok(routines);
                })
            .WithName("QueryProceduralRoutines")
            .WithTags("Procedural");

        return endpoints;
    }
}

public sealed record UpsertRoutineDto(
    Guid CompanionId,
    Guid? RoutineId,
    string Trigger,
    string Name,
    IReadOnlyList<string> Steps,
    IReadOnlyList<string> Checkpoints,
    string Outcome);
