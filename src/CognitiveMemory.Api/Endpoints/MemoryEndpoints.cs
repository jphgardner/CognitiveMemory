using System.Linq;
using CognitiveMemory.Application.Contracts;
using CognitiveMemory.Application.Interfaces;
using CognitiveMemory.Application.Services;

namespace CognitiveMemory.Api.Endpoints;

public static class MemoryEndpoints
{
    public static IEndpointRouteBuilder MapMemoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/memory");

        group.MapGet("/health", async (IMemoryService memoryService, CancellationToken cancellationToken) =>
        {
            var health = await memoryService.GetHealthAsync(cancellationToken);
            return Results.Ok(health);
        });

        group.MapGet("/claims", async (IMemoryService memoryService, CancellationToken cancellationToken) =>
        {
            var claims = await memoryService.GetClaimsAsync(cancellationToken);
            return Results.Ok(claims);
        });

        group.MapGet("/entities", async (IEntityRepository entityRepository, int? take, CancellationToken cancellationToken) =>
        {
            var entities = await entityRepository.GetRecentAsync(take ?? 50, cancellationToken);
            var payload = entities.Select(entity => new
            {
                entity.EntityId,
                entity.Type,
                entity.Name,
                entity.Aliases,
                entity.UpdatedAt
            });

            return Results.Ok(payload);
        });

        group.MapGet("/entities/{entityId:guid}", async (Guid entityId, IEntityRepository entityRepository, CancellationToken cancellationToken) =>
        {
            var entity = await entityRepository.GetByIdAsync(entityId, cancellationToken);
            if (entity is null)
            {
                return Results.NotFound();
            }

            return Results.Ok(entity);
        });

        group.MapPost("/claims", async (CreateClaimRequest request, IMemoryService memoryService, CancellationToken cancellationToken) =>
        {
            var created = await memoryService.CreateClaimAsync(request, cancellationToken);
            return Results.Created($"/api/v1/memory/claims/{created.ClaimId}", created);
        });

        group.MapPost("/claims/{claimId:guid}/supersede", async (Guid claimId, UpdateClaimStatusRequest request, IMemoryService memoryService, CancellationToken cancellationToken) =>
        {
            if (request.ReplacementClaimId is null || request.ReplacementClaimId == Guid.Empty)
            {
                return Results.BadRequest(new { error = "replacementClaimId is required." });
            }

            var updated = await memoryService.SupersedeClaimAsync(claimId, request.ReplacementClaimId.Value, cancellationToken);
            return Results.Ok(updated);
        });

        group.MapPost("/claims/{claimId:guid}/retract", async (Guid claimId, IMemoryService memoryService, CancellationToken cancellationToken) =>
        {
            var updated = await memoryService.RetractClaimAsync(claimId, cancellationToken);
            return Results.Ok(updated);
        });

        return app;
    }
}
