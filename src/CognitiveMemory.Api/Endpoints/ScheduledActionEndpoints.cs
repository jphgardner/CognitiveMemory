using System.Text.Json;
using CognitiveMemory.Infrastructure.Persistence;
using CognitiveMemory.Infrastructure.Persistence.Entities;
using CognitiveMemory.Infrastructure.Scheduling;
using CognitiveMemory.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace CognitiveMemory.Api.Endpoints;

public static class ScheduledActionEndpoints
{
    private static readonly JsonSerializerOptions SseJsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapScheduledActionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/scheduled-actions").WithTags("ScheduledActions").RequireAuthorization();

        group.MapPost(
                "/",
                async (HttpContext httpContext, CreateScheduledActionRequest request, IScheduledActionStore store, ScheduledActionOptions options, MemoryDbContext dbContext, CompanionOwnershipService ownershipService, CancellationToken cancellationToken) =>
                {
                    var companion = await ownershipService.ResolveOwnedCompanionAsync(httpContext.User, request.CompanionId, dbContext, cancellationToken);
                    if (companion is null)
                    {
                        return Results.NotFound();
                    }

                    if (string.IsNullOrWhiteSpace(request.ActionType))
                    {
                        return Results.BadRequest(new { error = "actionType is required." });
                    }

                    var inputJson = request.Input.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                        ? "{}"
                        : request.Input.GetRawText();

                    var created = await store.ScheduleAsync(
                        companion.SessionId,
                        request.ActionType.Trim(),
                        inputJson,
                        request.RunAtUtc,
                        request.MaxAttempts ?? options.DefaultMaxAttempts,
                        cancellationToken);
                    return Results.Ok(created);
                })
            .WithName("CreateScheduledAction")
            .WithTags("ScheduledActions");

        group.MapGet(
                "/",
                async (HttpContext httpContext, Guid companionId, string? status, int? take, IScheduledActionStore store, MemoryDbContext dbContext, CompanionOwnershipService ownershipService, CancellationToken cancellationToken) =>
                {
                    var companion = await ownershipService.ResolveOwnedCompanionAsync(httpContext.User, companionId, dbContext, cancellationToken);
                    if (companion is null)
                    {
                        return Results.NotFound();
                    }

                    var rows = await store.ListAsync(companion.SessionId, status, take ?? 100, cancellationToken);
                    return Results.Ok(rows);
                })
            .WithName("ListScheduledActions")
            .WithTags("ScheduledActions");

        group.MapGet(
                "/stream",
                async (
                    HttpContext context,
                    Guid companionId,
                    int? take,
                    MemoryDbContext dbContext,
                    CompanionOwnershipService ownershipService,
                    CancellationToken cancellationToken) =>
                {
                    var companion = await ownershipService.ResolveOwnedCompanionAsync(context.User, companionId, dbContext, cancellationToken);
                    if (companion is null)
                    {
                        context.Response.StatusCode = StatusCodes.Status404NotFound;
                        return;
                    }

                    var normalizedSessionId = companion.SessionId;
                    context.Response.StatusCode = StatusCodes.Status200OK;
                    context.Response.ContentType = "text/event-stream";
                    context.Response.Headers.CacheControl = "no-cache";
                    context.Response.Headers.Append("X-Accel-Buffering", "no");
                    await context.Response.StartAsync(cancellationToken);

                    string? lastSignature = null;
                    var keepAliveCounter = 0;
                    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var rows = await dbContext.ScheduledActions
                            .AsNoTracking()
                            .Where(x => x.SessionId == normalizedSessionId)
                            .OrderByDescending(x => x.CreatedAtUtc)
                            .Take(Math.Clamp(take ?? 250, 1, 500))
                            .ToArrayAsync(cancellationToken);

                        var signature = BuildSignature(rows);
                        if (!string.Equals(signature, lastSignature, StringComparison.Ordinal))
                        {
                            lastSignature = signature;
                            keepAliveCounter = 0;
                            await WriteSseEventAsync(context.Response, "snapshot", rows, cancellationToken);
                        }
                        else
                        {
                            keepAliveCounter += 1;
                            if (keepAliveCounter >= 5)
                            {
                                keepAliveCounter = 0;
                                await WriteSseCommentAsync(context.Response, "keep-alive", cancellationToken);
                            }
                        }

                        var advanced = await timer.WaitForNextTickAsync(cancellationToken);
                        if (!advanced)
                        {
                            break;
                        }
                    }
                })
            .WithName("StreamScheduledActions")
            .WithTags("ScheduledActions");

        group.MapPost(
                "/{actionId:guid}/cancel",
                async (HttpContext httpContext, Guid companionId, Guid actionId, IScheduledActionStore store, MemoryDbContext dbContext, CompanionOwnershipService ownershipService, CancellationToken cancellationToken) =>
                {
                    var companion = await ownershipService.ResolveOwnedCompanionAsync(httpContext.User, companionId, dbContext, cancellationToken);
                    if (companion is null)
                    {
                        return Results.NotFound();
                    }

                    var ownedAction = await dbContext.ScheduledActions
                        .AsNoTracking()
                        .AnyAsync(x => x.ActionId == actionId && x.SessionId == companion.SessionId, cancellationToken);
                    if (!ownedAction)
                    {
                        return Results.NotFound();
                    }

                    var canceled = await store.CancelAsync(actionId, cancellationToken);
                    return canceled ? Results.Ok(new { actionId, status = ScheduledActionStatus.Canceled }) : Results.NotFound();
                })
            .WithName("CancelScheduledAction")
            .WithTags("ScheduledActions");

        return endpoints;
    }

    private static string BuildSignature(IReadOnlyList<ScheduledActionEntity> rows)
    {
        if (rows.Count == 0)
        {
            return "empty";
        }

        return string.Join(
            '|',
            rows.Select(
                x => $"{x.ActionId:N}:{x.Status}:{x.Attempts}:{x.UpdatedAtUtc.ToUnixTimeMilliseconds()}:{x.CompletedAtUtc?.ToUnixTimeMilliseconds() ?? 0}"));
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
}

public sealed record CreateScheduledActionRequest(
    Guid CompanionId,
    string ActionType,
    DateTimeOffset RunAtUtc,
    JsonElement Input,
    int? MaxAttempts);
