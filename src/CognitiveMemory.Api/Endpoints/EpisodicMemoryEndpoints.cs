using CognitiveMemory.Application.Episodic;

namespace CognitiveMemory.Api.Endpoints;

public static class EpisodicMemoryEndpoints
{
    public static IEndpointRouteBuilder MapEpisodicMemoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/episodic/events",
                async (AppendEpisodicEventDto request, IEpisodicMemoryService service, CancellationToken cancellationToken) =>
                {
                    var created = await service.AppendAsync(
                        new AppendEpisodicMemoryRequest(
                            request.SessionId,
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

        endpoints.MapGet(
                "/api/episodic/events/{sessionId}",
                async (
                    string sessionId,
                    DateTimeOffset? fromUtc,
                    DateTimeOffset? toUtc,
                    int? take,
                    IEpisodicMemoryService service,
                    CancellationToken cancellationToken) =>
                {
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
    string SessionId,
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
