using CognitiveMemory.Infrastructure.Persistence;
using CognitiveMemory.Api.Security;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CognitiveMemory.Api.Endpoints;

public static class EventingEndpoints
{
    private static readonly JsonSerializerOptions SseJsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapEventingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/eventing").WithTags("Eventing").RequireAuthorization();

        group
            .MapGet(
                "/events",
                async (
                    HttpContext httpContext,
                    Guid companionId,
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

                    var result = await QuerySessionEventsAsync(
                        dbContext,
                        companion.SessionId,
                        take,
                        cancellationToken);

                    return Results.Ok(result);
                })
            .WithName("GetEventingEvents")
            .WithTags("Eventing");

        group
            .MapGet(
                "/events/stream",
                async (
                    HttpContext httpContext,
                    Guid companionId,
                    int? take,
                    MemoryDbContext dbContext,
                    CompanionOwnershipService ownershipService,
                    CancellationToken cancellationToken) =>
                {
                    var companion = await ownershipService.ResolveOwnedCompanionAsync(httpContext.User, companionId, dbContext, cancellationToken);
                    if (companion is null)
                    {
                        httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
                        return;
                    }

                    httpContext.Response.StatusCode = StatusCodes.Status200OK;
                    httpContext.Response.ContentType = "text/event-stream";
                    httpContext.Response.Headers.CacheControl = "no-cache";
                    httpContext.Response.Headers.Append("X-Accel-Buffering", "no");
                    await httpContext.Response.StartAsync(cancellationToken);

                    string? lastSignature = null;
                    var keepAliveEvery = 5;
                    var cycles = 0;
                    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var rows = await QuerySessionEventsAsync(
                            dbContext,
                            companion.SessionId,
                            take,
                            cancellationToken);

                        var signature = BuildSignature(rows);
                        if (!string.Equals(signature, lastSignature, StringComparison.Ordinal))
                        {
                            lastSignature = signature;
                            cycles = 0;
                            await WriteSseEventAsync(httpContext.Response, "snapshot", rows, cancellationToken);
                        }
                        else
                        {
                            cycles += 1;
                            if (cycles >= keepAliveEvery)
                            {
                                cycles = 0;
                                await WriteSseCommentAsync(httpContext.Response, "keep-alive", cancellationToken);
                            }
                        }

                        var advanced = await timer.WaitForNextTickAsync(cancellationToken);
                        if (!advanced)
                        {
                            break;
                        }
                    }
                })
            .WithName("GetEventingEventsStream")
            .WithTags("Eventing");

        return endpoints;
    }
    private static async Task<IReadOnlyList<EventingEventDto>> QuerySessionEventsAsync(
        MemoryDbContext dbContext,
        string sessionId,
        int? take,
        CancellationToken cancellationToken)
    {
        var requestedTake = Math.Clamp(take ?? 120, 10, 500);
        var normalizedSessionId = sessionId.Trim();
        var pattern = SqlLikePattern.Contains(normalizedSessionId);

        var rows = await dbContext.OutboxMessages
            .AsNoTracking()
            .Where(x => EF.Functions.ILike(x.PayloadJson, pattern))
            .OrderByDescending(x => x.OccurredAtUtc)
            .Take(requestedTake)
            .ToArrayAsync(cancellationToken);

        if (rows.Length == 0)
        {
            return [];
        }

        var eventIds = rows.Select(x => x.EventId).ToArray();
        var checkpointCounts = await dbContext.EventConsumerCheckpoints
            .AsNoTracking()
            .Where(x => eventIds.Contains(x.EventId))
            .GroupBy(x => x.EventId)
            .Select(x => new { EventId = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.EventId, x => x.Count, cancellationToken);

        return rows.Select(
                x => new EventingEventDto(
                    x.EventId,
                    x.EventType,
                    x.AggregateType,
                    x.AggregateId,
                    x.Status,
                    x.RetryCount,
                    x.LastError,
                    x.OccurredAtUtc,
                    x.LastAttemptedAtUtc,
                    x.PublishedAtUtc,
                    checkpointCounts.TryGetValue(x.EventId, out var count) ? count : 0,
                    BuildPayloadPreview(x.PayloadJson)))
            .ToArray();
    }

    private static string BuildSignature(IReadOnlyList<EventingEventDto> rows)
    {
        if (rows.Count == 0)
        {
            return "empty";
        }

        var parts = rows.Select(
            x => $"{x.EventId:N}:{x.Status}:{x.RetryCount}:{x.ConsumerCheckpointCount}:{x.LastAttemptedAtUtc?.ToUnixTimeMilliseconds() ?? 0}:{x.PublishedAtUtc?.ToUnixTimeMilliseconds() ?? 0}");
        return string.Join('|', parts);
    }

    private static async Task WriteSseEventAsync(HttpResponse response, string eventName, object payload, CancellationToken cancellationToken)
    {
        var serialized = JsonSerializer.Serialize(payload, SseJsonOptions);
        await response.WriteAsync($"event: {eventName}\n", cancellationToken);
        await response.WriteAsync($"data: {serialized}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    private static async Task WriteSseCommentAsync(HttpResponse response, string comment, CancellationToken cancellationToken)
    {
        await response.WriteAsync($": {comment}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    private static string BuildPayloadPreview(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return string.Empty;
        }

        var compact = payloadJson.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return compact.Length <= 220 ? compact : $"{compact[..220]}...";
    }

}

public sealed record EventingEventDto(
    Guid EventId,
    string EventType,
    string AggregateType,
    string AggregateId,
    string Status,
    int RetryCount,
    string? LastError,
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset? LastAttemptedAtUtc,
    DateTimeOffset? PublishedAtUtc,
    int ConsumerCheckpointCount,
    string PayloadPreview);
