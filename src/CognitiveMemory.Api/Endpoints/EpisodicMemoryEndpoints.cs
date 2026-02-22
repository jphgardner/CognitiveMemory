using CognitiveMemory.Application.Episodic;
using CognitiveMemory.Api.Security;
using CognitiveMemory.Infrastructure.Persistence;

namespace CognitiveMemory.Api.Endpoints;

public static class EpisodicMemoryEndpoints
{
    public static IEndpointRouteBuilder MapEpisodicMemoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/episodic").WithTags("Episodic").RequireAuthorization();

        group.MapPost(
                "/events",
                async (HttpContext httpContext, AppendEpisodicEventDto request, IEpisodicMemoryService service, MemoryDbContext dbContext, CompanionOwnershipService ownershipService, CancellationToken cancellationToken) =>
                {
                    var companion = await ownershipService.ResolveOwnedCompanionAsync(httpContext.User, request.CompanionId, dbContext, cancellationToken);
                    if (companion is null)
                    {
                        return Results.NotFound();
                    }

                    var created = await service.AppendAsync(
                        new AppendEpisodicMemoryRequest(
                            companion.SessionId,
                            request.Who,
                            request.What,
                            request.Context,
                            request.SourceReference,
                            request.OccurredAtUtc),
                        cancellationToken);

                    return Results.Ok(ToDto(created));
                })
            .WithName("AppendEpisodicEvent")
            .WithTags("Episodic");

        group.MapGet(
                "/events/{sessionId}",
                async (
                    HttpContext httpContext,
                    Guid companionId,
                    string sessionId,
                    DateTimeOffset? fromUtc,
                    DateTimeOffset? toUtc,
                    int? take,
                    IEpisodicMemoryService service,
                    MemoryDbContext dbContext,
                    CompanionOwnershipService ownershipService,
                    CancellationToken cancellationToken) =>
                {
                    var companion = await ownershipService.ResolveOwnedCompanionAsync(httpContext.User, companionId, dbContext, cancellationToken);
                    if (companion is null)
                    {
                        return Results.NotFound();
                    }

                    if (!string.Equals(companion.SessionId, sessionId, StringComparison.Ordinal))
                    {
                        return Results.BadRequest(new { error = "sessionId does not match companion scope." });
                    }

                    var events = await service.QueryBySessionAsync(
                        sessionId,
                        fromUtc,
                        toUtc,
                        take ?? 100,
                        cancellationToken);

                    return Results.Ok(events.Select(ToDto));
                })
            .WithName("GetEpisodicEventsBySession")
            .WithTags("Episodic");

        return endpoints;
    }

    private static EpisodicEventDto ToDto(CognitiveMemory.Domain.Memory.EpisodicMemoryEvent memoryEvent) =>
        new(
            memoryEvent.EventId,
            memoryEvent.SessionId,
            memoryEvent.Who,
            memoryEvent.What,
            memoryEvent.OccurredAt,
            memoryEvent.Context,
            memoryEvent.SourceReference);
}

public sealed record AppendEpisodicEventDto(
    Guid CompanionId,
    string Who,
    string What,
    string Context,
    string SourceReference,
    DateTimeOffset? OccurredAtUtc = null);

public sealed record EpisodicEventDto(
    Guid EventId,
    string SessionId,
    string Who,
    string What,
    DateTimeOffset OccurredAtUtc,
    string Context,
    string SourceReference);
