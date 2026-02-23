using System.Text.Json;
using CognitiveMemory.Api.Security;
using CognitiveMemory.Infrastructure.Persistence;
using CognitiveMemory.Infrastructure.Subconscious;
using Microsoft.EntityFrameworkCore;

namespace CognitiveMemory.Api.Endpoints;

public static class SubconsciousDebateEndpoints
{
    private static readonly JsonSerializerOptions SseJsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapSubconsciousDebateEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/subconscious").WithTags("Subconscious").RequireAuthorization();

        group.MapGet(
                "/debates/{sessionId}",
                async (HttpContext httpContext, string sessionId, int? take, MemoryDbContext dbContext, CompanionOwnershipService ownershipService, CancellationToken cancellationToken) =>
                {
                    var companion = await ownershipService.ResolveOwnedCompanionBySessionAsync(httpContext.User, sessionId, dbContext, cancellationToken);
                    if (companion is null)
                    {
                        return Results.NotFound();
                    }

                    var rows = await dbContext.SubconsciousDebateSessions
                        .AsNoTracking()
                        .Where(x => x.SessionId == companion.SessionId)
                        .OrderByDescending(x => x.CreatedAtUtc)
                        .Take(Math.Clamp(take ?? 40, 1, 300))
                        .ToArrayAsync(cancellationToken);

                    return Results.Ok(rows);
                })
            .WithName("GetSubconsciousDebatesBySession")
            .WithTags("Subconscious");

        group.MapGet(
                "/debates/detail/{debateId:guid}",
                async (HttpContext httpContext, Guid debateId, MemoryDbContext dbContext, CompanionOwnershipService ownershipService, CancellationToken cancellationToken) =>
                {
                    var row = await dbContext.SubconsciousDebateSessions.AsNoTracking().FirstOrDefaultAsync(x => x.DebateId == debateId, cancellationToken);
                    if (row is null)
                    {
                        return Results.NotFound();
                    }

                    var companion = await ownershipService.ResolveOwnedCompanionBySessionAsync(httpContext.User, row.SessionId, dbContext, cancellationToken);
                    return companion is null ? Results.NotFound() : Results.Ok(row);
                })
            .WithName("GetSubconsciousDebate")
            .WithTags("Subconscious");

        group.MapGet(
                "/debates/{debateId:guid}/turns",
                async (HttpContext httpContext, Guid debateId, MemoryDbContext dbContext, CompanionOwnershipService ownershipService, CancellationToken cancellationToken) =>
                {
                    var session = await dbContext.SubconsciousDebateSessions
                        .AsNoTracking()
                        .FirstOrDefaultAsync(x => x.DebateId == debateId, cancellationToken);
                    if (session is null)
                    {
                        return Results.NotFound();
                    }

                    var companion = await ownershipService.ResolveOwnedCompanionBySessionAsync(httpContext.User, session.SessionId, dbContext, cancellationToken);
                    if (companion is null)
                    {
                        return Results.NotFound();
                    }

                    var rows = await dbContext.SubconsciousDebateTurns
                        .AsNoTracking()
                        .Where(x => x.DebateId == debateId && x.CompanionId == companion.CompanionId)
                        .OrderBy(x => x.TurnNumber)
                        .ToArrayAsync(cancellationToken);
                    return Results.Ok(rows);
                })
            .WithName("GetSubconsciousDebateTurns")
            .WithTags("Subconscious");

        group.MapGet(
                "/debates/{debateId:guid}/outcome",
                async (HttpContext httpContext, Guid debateId, MemoryDbContext dbContext, CompanionOwnershipService ownershipService, CancellationToken cancellationToken) =>
                {
                    var session = await dbContext.SubconsciousDebateSessions
                        .AsNoTracking()
                        .FirstOrDefaultAsync(x => x.DebateId == debateId, cancellationToken);
                    if (session is null)
                    {
                        return Results.NotFound();
                    }

                    var companion = await ownershipService.ResolveOwnedCompanionBySessionAsync(httpContext.User, session.SessionId, dbContext, cancellationToken);
                    if (companion is null)
                    {
                        return Results.NotFound();
                    }

                    var row = await dbContext.SubconsciousDebateOutcomes
                        .AsNoTracking()
                        .FirstOrDefaultAsync(x => x.DebateId == debateId && x.CompanionId == companion.CompanionId, cancellationToken);
                    return row is null ? Results.NotFound() : Results.Ok(row);
                })
            .WithName("GetSubconsciousDebateOutcome")
            .WithTags("Subconscious");

        group.MapGet(
                "/debates/{debateId:guid}/review",
                async (
                    HttpContext httpContext,
                    Guid debateId,
                    MemoryDbContext dbContext,
                    CompanionOwnershipService ownershipService,
                    ISubconsciousOutcomeValidator outcomeValidator,
                    ISubconsciousOutcomeApplier outcomeApplier,
                    CancellationToken cancellationToken) =>
                {
                    var session = await dbContext.SubconsciousDebateSessions
                        .AsNoTracking()
                        .FirstOrDefaultAsync(x => x.DebateId == debateId, cancellationToken);
                    if (session is null)
                    {
                        return Results.NotFound();
                    }

                    var outcomeRow = await dbContext.SubconsciousDebateOutcomes
                        .AsNoTracking()
                        .FirstOrDefaultAsync(x => x.DebateId == debateId && x.CompanionId == session.CompanionId, cancellationToken);
                    if (outcomeRow is null)
                    {
                        return Results.NotFound();
                    }
                    var companion = await ownershipService.ResolveOwnedCompanionBySessionAsync(httpContext.User, session.SessionId, dbContext, cancellationToken);
                    if (companion is null)
                    {
                        return Results.NotFound();
                    }

                    var validation = outcomeValidator.Validate(outcomeRow.OutcomeJson);
                    if (!validation.IsValid || validation.Outcome is null)
                    {
                        return Results.Ok(
                            new
                            {
                                debateId,
                                sessionId = session.SessionId,
                                validation,
                                applyPreview = (SubconsciousApplyReport?)null
                            });
                    }

                    var preview = await outcomeApplier.PreviewAsync(debateId, session.SessionId, validation.Outcome, cancellationToken);
                    return Results.Ok(
                        new
                        {
                            debateId,
                            sessionId = session.SessionId,
                            validation,
                            applyPreview = preview
                        });
                })
            .WithName("ReviewSubconsciousDebate")
            .WithTags("Subconscious");

        group.MapGet(
                "/debates/{debateId:guid}/events",
                async (HttpContext httpContext, Guid debateId, int? take, MemoryDbContext dbContext, CompanionOwnershipService ownershipService, CancellationToken cancellationToken) =>
                {
                    var session = await dbContext.SubconsciousDebateSessions
                        .AsNoTracking()
                        .FirstOrDefaultAsync(x => x.DebateId == debateId, cancellationToken);
                    if (session is null)
                    {
                        return Results.NotFound();
                    }

                    var companion = await ownershipService.ResolveOwnedCompanionBySessionAsync(httpContext.User, session.SessionId, dbContext, cancellationToken);
                    if (companion is null)
                    {
                        return Results.NotFound();
                    }

                    var aggregateId = debateId.ToString("N");
                    var debateTokenPattern = SqlLikePattern.Contains(debateId.ToString("D"));
                    var rows = await dbContext.OutboxMessages
                        .AsNoTracking()
                        .Where(
                            x => x.AggregateType == "SubconsciousDebate"
                                 && (x.AggregateId == aggregateId || EF.Functions.ILike(x.PayloadJson, debateTokenPattern)))
                        .OrderByDescending(x => x.OccurredAtUtc)
                        .Take(Math.Clamp(take ?? 120, 1, 500))
                        .Select(
                            x => new
                            {
                                x.EventId,
                                x.EventType,
                                x.Status,
                                x.RetryCount,
                                x.LastError,
                                x.OccurredAtUtc,
                                x.PublishedAtUtc,
                                PayloadPreview = BuildPayloadPreview(x.PayloadJson)
                            })
                        .ToArrayAsync(cancellationToken);

                    return Results.Ok(rows);
                })
            .WithName("GetSubconsciousDebateEvents")
            .WithTags("Subconscious");

        group.MapPost(
                "/debates/{debateId:guid}/approve",
                async (HttpContext httpContext, Guid debateId, MemoryDbContext dbContext, CompanionOwnershipService ownershipService, ISubconsciousDebateService debateService, CancellationToken cancellationToken) =>
                {
                    var session = await dbContext.SubconsciousDebateSessions.AsNoTracking().FirstOrDefaultAsync(x => x.DebateId == debateId, cancellationToken);
                    if (session is null || await ownershipService.ResolveOwnedCompanionBySessionAsync(httpContext.User, session.SessionId, dbContext, cancellationToken) is null)
                    {
                        return Results.NotFound();
                    }

                    var ok = await debateService.ApproveDebateAsync(debateId, cancellationToken);
                    return ok ? Results.Ok(new { debateId, status = "approved" }) : Results.NotFound();
                })
            .WithName("ApproveSubconsciousDebate")
            .WithTags("Subconscious");

        group.MapPost(
                "/debates/{debateId:guid}/decision",
                async (
                    HttpContext httpContext,
                    Guid debateId,
                    ResolveSubconsciousDebateDecisionRequest request,
                    MemoryDbContext dbContext,
                    CompanionOwnershipService ownershipService,
                    ISubconsciousDebateService debateService,
                    CancellationToken cancellationToken) =>
                {
                    var session = await dbContext.SubconsciousDebateSessions.FirstOrDefaultAsync(x => x.DebateId == debateId, cancellationToken);
                    var outcome = await dbContext.SubconsciousDebateOutcomes.FirstOrDefaultAsync(x => x.DebateId == debateId, cancellationToken);
                    if (session is null || outcome is null)
                    {
                        return Results.NotFound();
                    }
                    if (outcome.CompanionId != session.CompanionId)
                    {
                        return Results.NotFound();
                    }
                    if (await ownershipService.ResolveOwnedCompanionBySessionAsync(httpContext.User, session.SessionId, dbContext, cancellationToken) is null)
                    {
                        return Results.NotFound();
                    }

                    var action = request.Action?.Trim().ToLowerInvariant();
                    var userInput = request.UserInput?.Trim();
                    var queueRerun = request.QueueRerun == true && !string.IsNullOrWhiteSpace(userInput);

                    var success = action switch
                    {
                        "approve" => await debateService.ApproveDebateAsync(debateId, cancellationToken),
                        "reject" => await debateService.RejectDebateAsync(debateId, cancellationToken),
                        _ => false
                    };

                    if (!success)
                    {
                        return Results.BadRequest("decision action must be 'approve' or 'reject'.");
                    }

                    if (!string.IsNullOrWhiteSpace(userInput))
                    {
                        var prefix = action == "approve" ? "UserDecisionNote(approved): " : "UserDecisionNote(rejected): ";
                        outcome.ApplyError = string.IsNullOrWhiteSpace(outcome.ApplyError)
                            ? $"{prefix}{userInput}"
                            : $"{outcome.ApplyError}{Environment.NewLine}{prefix}{userInput}";
                        outcome.UpdatedAtUtc = DateTimeOffset.UtcNow;
                    }

                    if (queueRerun)
                    {
                        await debateService.QueueDebateAsync(
                            session.SessionId,
                            new SubconsciousDebateTopic(
                                TopicKey: session.TopicKey,
                                TriggerEventType: "SubconsciousUserDecision",
                                TriggerEventId: null,
                                TriggerPayloadJson: JsonSerializer.Serialize(
                                    new
                                    {
                                        priorDebateId = debateId,
                                        decision = action,
                                        userInput,
                                        atUtc = DateTimeOffset.UtcNow
                                    },
                                    SseJsonOptions)),
                            cancellationToken);
                    }

                    await dbContext.SaveChangesAsync(cancellationToken);
                    return Results.Ok(
                        new
                        {
                            debateId,
                            action,
                            userInput,
                            queueRerun,
                            status = action == "approve" ? "approved" : "rejected"
                        });
                })
            .WithName("ResolveSubconsciousDebateDecision")
            .WithTags("Subconscious");

        group.MapPost(
                "/debates/{debateId:guid}/reject",
                async (HttpContext httpContext, Guid debateId, MemoryDbContext dbContext, CompanionOwnershipService ownershipService, ISubconsciousDebateService debateService, CancellationToken cancellationToken) =>
                {
                    var session = await dbContext.SubconsciousDebateSessions.AsNoTracking().FirstOrDefaultAsync(x => x.DebateId == debateId, cancellationToken);
                    if (session is null || await ownershipService.ResolveOwnedCompanionBySessionAsync(httpContext.User, session.SessionId, dbContext, cancellationToken) is null)
                    {
                        return Results.NotFound();
                    }

                    var ok = await debateService.RejectDebateAsync(debateId, cancellationToken);
                    return ok ? Results.Ok(new { debateId, status = "rejected" }) : Results.NotFound();
                })
            .WithName("RejectSubconsciousDebate")
            .WithTags("Subconscious");

        group.MapPost(
                "/debates/run-once",
                async (HttpContext httpContext, RunSubconsciousDebateRequest request, MemoryDbContext dbContext, CompanionOwnershipService ownershipService, ISubconsciousDebateService debateService, CancellationToken cancellationToken) =>
                {
                    if (string.IsNullOrWhiteSpace(request.SessionId))
                    {
                        return Results.BadRequest("sessionId is required.");
                    }

                    var companion = await ownershipService.ResolveOwnedCompanionBySessionAsync(httpContext.User, request.SessionId, dbContext, cancellationToken);
                    if (companion is null)
                    {
                        return Results.NotFound();
                    }

                    await debateService.QueueDebateAsync(
                        companion.SessionId,
                        new SubconsciousDebateTopic(
                            request.TopicKey?.Trim() is { Length: > 0 } topic ? topic : "manual",
                            request.TriggerEventType?.Trim() is { Length: > 0 } trigger ? trigger : "ManualRun",
                            null,
                            request.TriggerPayloadJson ?? "{}"),
                        cancellationToken);
                    return Results.Accepted();
                })
            .WithName("RunSubconsciousDebateOnce")
            .WithTags("Subconscious");

        group.MapGet(
                "/debates/stream",
                async (HttpContext context, string sessionId, int? take, MemoryDbContext dbContext, CompanionOwnershipService ownershipService, CancellationToken cancellationToken) =>
                {
                    if (string.IsNullOrWhiteSpace(sessionId))
                    {
                        context.Response.StatusCode = StatusCodes.Status400BadRequest;
                        await context.Response.WriteAsync("sessionId is required.", cancellationToken);
                        return;
                    }

                    var companion = await ownershipService.ResolveOwnedCompanionBySessionAsync(context.User, sessionId, dbContext, cancellationToken);
                    if (companion is null)
                    {
                        context.Response.StatusCode = StatusCodes.Status404NotFound;
                        return;
                    }

                    sessionId = companion.SessionId;
                    context.Response.StatusCode = StatusCodes.Status200OK;
                    context.Response.ContentType = "text/event-stream";
                    context.Response.Headers.CacheControl = "no-cache";
                    context.Response.Headers.Append("X-Accel-Buffering", "no");
                    await context.Response.StartAsync(cancellationToken);

                    string? lastSignature = null;
                    var emittedLifecycleEventIds = new HashSet<Guid>();
                    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
                    var keepAliveCounter = 0;
                    var sessionTokenPattern = SqlLikePattern.Contains(sessionId.Trim());

                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var rows = await dbContext.SubconsciousDebateSessions
                            .AsNoTracking()
                            .Where(x => x.SessionId == sessionId)
                            .OrderByDescending(x => x.CreatedAtUtc)
                            .Take(Math.Clamp(take ?? 30, 1, 200))
                            .Select(x => new
                            {
                                x.DebateId,
                                x.TopicKey,
                                x.TriggerEventType,
                                x.State,
                                x.CreatedAtUtc,
                                x.UpdatedAtUtc,
                                x.LastError
                            })
                            .ToArrayAsync(cancellationToken);

                        var signature = string.Join('|', rows.Select(x => $"{x.DebateId:N}:{x.State}:{x.UpdatedAtUtc.ToUnixTimeMilliseconds()}"));
                        if (signature != lastSignature)
                        {
                            lastSignature = signature;
                            keepAliveCounter = 0;
                            await WriteEventAsync(context.Response, "snapshot", rows, cancellationToken);
                        }
                        else
                        {
                            keepAliveCounter += 1;
                            if (keepAliveCounter >= 5)
                            {
                                keepAliveCounter = 0;
                                await context.Response.WriteAsync(": keep-alive\n\n", cancellationToken);
                                await context.Response.Body.FlushAsync(cancellationToken);
                            }
                        }

                        var lifecycleRows = await dbContext.OutboxMessages
                            .AsNoTracking()
                            .Where(x => x.AggregateType == "SubconsciousDebate"
                                        && EF.Functions.ILike(x.PayloadJson, sessionTokenPattern))
                            .OrderBy(x => x.OccurredAtUtc)
                            .ThenBy(x => x.EventId)
                            .Take(120)
                            .Select(x => new
                            {
                                x.EventId,
                                x.EventType,
                                x.Status,
                                x.LastError,
                                x.OccurredAtUtc,
                                x.PublishedAtUtc,
                                PayloadPreview = BuildPayloadPreview(x.PayloadJson)
                            })
                            .ToArrayAsync(cancellationToken);

                        foreach (var lifecycle in lifecycleRows)
                        {
                            if (!emittedLifecycleEventIds.Add(lifecycle.EventId))
                            {
                                continue;
                            }

                            await WriteEventAsync(context.Response, "lifecycle", lifecycle, cancellationToken);
                        }

                        if (emittedLifecycleEventIds.Count > 1000)
                        {
                            emittedLifecycleEventIds = lifecycleRows
                                .TakeLast(300)
                                .Select(x => x.EventId)
                                .ToHashSet();
                        }

                        var next = await timer.WaitForNextTickAsync(cancellationToken);
                        if (!next)
                        {
                            break;
                        }
                    }
                })
            .WithName("StreamSubconsciousDebates")
            .WithTags("Subconscious");

        return endpoints;
    }

    private static async Task WriteEventAsync(HttpResponse response, string eventName, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, SseJsonOptions);
        await response.WriteAsync($"event: {eventName}\n", cancellationToken);
        await response.WriteAsync($"data: {json}\n\n", cancellationToken);
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

public sealed record RunSubconsciousDebateRequest(string SessionId, string? TopicKey, string? TriggerEventType, string? TriggerPayloadJson);
public sealed record ResolveSubconsciousDebateDecisionRequest(string Action, string? UserInput, bool? QueueRerun);
