using CognitiveMemory.Application.Abstractions;
using CognitiveMemory.Api.Security;
using CognitiveMemory.Infrastructure.Persistence;

namespace CognitiveMemory.Api.Endpoints;

public static class SelfModelEndpoints
{
    public static IEndpointRouteBuilder MapSelfModelEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/self-model").WithTags("SelfModel").RequireAuthorization();

        group.MapGet(
                "/preferences",
                async (HttpContext httpContext, Guid companionId, ISelfModelRepository repository, MemoryDbContext dbContext, CompanionOwnershipService ownershipService, CancellationToken cancellationToken) =>
                {
                    var companion = await ownershipService.ResolveOwnedCompanionAsync(httpContext.User, companionId, dbContext, cancellationToken);
                    if (companion is null)
                    {
                        return Results.NotFound();
                    }

                    var snapshot = await repository.GetAsync(companion.CompanionId, cancellationToken);
                    return Results.Ok(snapshot);
                })
            .WithName("GetSelfModelPreferences")
            .WithTags("SelfModel");

        group.MapPost(
                "/preferences",
                async (HttpContext httpContext, SetPreferenceDto request, ISelfModelRepository repository, MemoryDbContext dbContext, CompanionOwnershipService ownershipService, CancellationToken cancellationToken) =>
                {
                    var companion = await ownershipService.ResolveOwnedCompanionAsync(httpContext.User, request.CompanionId, dbContext, cancellationToken);
                    if (companion is null)
                    {
                        return Results.NotFound();
                    }

                    await repository.SetPreferenceAsync(companion.CompanionId, request.Key, request.Value, cancellationToken);
                    return Results.NoContent();
                })
            .WithName("SetSelfModelPreference")
            .WithTags("SelfModel");

        return endpoints;
    }
}

public sealed record SetPreferenceDto(Guid CompanionId, string Key, string Value);
