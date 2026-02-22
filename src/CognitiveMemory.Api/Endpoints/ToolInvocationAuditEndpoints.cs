using CognitiveMemory.Application.Abstractions;
using CognitiveMemory.Api.Security;
using CognitiveMemory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CognitiveMemory.Api.Endpoints;

public static class ToolInvocationAuditEndpoints
{
    public static IEndpointRouteBuilder MapToolInvocationAuditEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/tool-invocations").WithTags("ToolInvocations").RequireAuthorization();

        group
            .MapGet(
                "/",
                async (
                    HttpContext httpContext,
                    Guid companionId,
                    DateTimeOffset? fromUtc,
                    DateTimeOffset? toUtc,
                    int? take,
                    MemoryDbContext dbContext,
                    CompanionOwnershipService ownershipService,
                    CancellationToken cancellationToken) =>
                {
                    var companion = await ownershipService.ResolveOwnedCompanionAsync(httpContext.User, companionId, dbContext, cancellationToken);
                    if (companion is null)
                    {
                        return Results.NotFound();
                    }

                    var to = toUtc ?? DateTimeOffset.UtcNow;
                    var from = fromUtc ?? to.AddHours(-1);
                    var token = companion.SessionId.ToLowerInvariant();
                    var rows = await dbContext.ToolInvocationAudits
                        .AsNoTracking()
                        .Where(x => x.ExecutedAtUtc >= from && x.ExecutedAtUtc <= to)
                        .Where(x => x.CompanionId == companion.CompanionId || x.ArgumentsJson.ToLower().Contains(token))
                        .OrderByDescending(x => x.ExecutedAtUtc)
                        .Take(Math.Clamp(take ?? 50, 1, 200))
                        .ToListAsync(cancellationToken);

                    return Results.Ok(rows);
                })
            .WithName("GetToolInvocations")
            .WithTags("ToolInvocations");

        return endpoints;
    }
}
