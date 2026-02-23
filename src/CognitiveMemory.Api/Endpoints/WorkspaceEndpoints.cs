using CognitiveMemory.Api.Security;
using CognitiveMemory.Domain.Memory;
using CognitiveMemory.Infrastructure.Persistence;
using CognitiveMemory.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace CognitiveMemory.Api.Endpoints;

public static class WorkspaceEndpoints
{
    public static IEndpointRouteBuilder MapWorkspaceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/workspace").WithTags("Workspace").RequireAuthorization();

        group.MapGet(
            "/companion/{companionId:guid}/summary",
            async (HttpContext httpContext, Guid companionId, MemoryDbContext dbContext, CompanionOwnershipService ownershipService, CancellationToken cancellationToken) =>
            {
                var companion = await ownershipService.ResolveOwnedCompanionAsync(httpContext.User, companionId, dbContext, cancellationToken);
                if (companion is null)
                {
                    return Results.NotFound();
                }

                var sessionId = companion.SessionId;
                var semanticCount = await dbContext.SemanticClaims.CountAsync(x => x.Subject == $"session:{sessionId}", cancellationToken);
                var episodicCount = await dbContext.EpisodicMemoryEvents.CountAsync(x => x.SessionId == sessionId, cancellationToken);
                var relationshipCount = await dbContext.MemoryRelationships.CountAsync(
                    x => x.SessionId == sessionId
                         && x.Status == "Active"
                         && !(x.FromType == x.ToType && x.FromId == x.ToId),
                    cancellationToken);
                var debateCount = await dbContext.SubconsciousDebateSessions.CountAsync(x => x.SessionId == sessionId, cancellationToken);
                var lastDebate = await dbContext.SubconsciousDebateSessions
                    .AsNoTracking()
                    .Where(x => x.SessionId == sessionId)
                    .OrderByDescending(x => x.UpdatedAtUtc)
                    .Select(
                        x => new WorkspaceDebateSummaryDto(
                            x.DebateId,
                            x.TopicKey,
                            x.State,
                            x.UpdatedAtUtc,
                            x.LastError))
                    .FirstOrDefaultAsync(cancellationToken);

                var lastActivityUtc = await ResolveLastActivityUtcAsync(sessionId, dbContext, cancellationToken);

                var summary = new CompanionWorkspaceSummaryDto(
                    companion.CompanionId,
                    companion.Name,
                    companion.Tone,
                    companion.Purpose,
                    companion.ModelHint,
                    companion.SessionId,
                    companion.OriginStory,
                    companion.BirthDateUtc,
                    companion.CreatedAtUtc,
                    companion.UpdatedAtUtc,
                    semanticCount,
                    episodicCount,
                    relationshipCount,
                    debateCount,
                    lastDebate,
                    lastActivityUtc);

                return Results.Ok(summary);
            });

        group.MapGet(
            "/companion/{companionId:guid}/memory-packet",
            async (HttpContext httpContext, Guid companionId, int? take, MemoryDbContext dbContext, CompanionOwnershipService ownershipService, CancellationToken cancellationToken) =>
            {
                var companion = await ownershipService.ResolveOwnedCompanionAsync(httpContext.User, companionId, dbContext, cancellationToken);
                if (companion is null)
                {
                    return Results.NotFound();
                }

                var boundedTake = Math.Clamp(take ?? 20, 5, 100);
                var subject = $"session:{companion.SessionId}";

                var claims = await dbContext.SemanticClaims
                    .AsNoTracking()
                    .Where(x => x.Subject == subject)
                    .OrderByDescending(x => x.UpdatedAtUtc)
                    .Take(boundedTake)
                    .Select(
                        x => new WorkspaceClaimDto(
                            x.ClaimId,
                            x.Predicate,
                            x.Value,
                            x.Confidence,
                            x.Status,
                            x.UpdatedAtUtc))
                    .ToListAsync(cancellationToken);

                var contradictions = await dbContext.ClaimContradictions
                    .AsNoTracking()
                    .Where(
                        x => dbContext.SemanticClaims.Any(c => c.ClaimId == x.ClaimAId && c.Subject == subject)
                             || dbContext.SemanticClaims.Any(c => c.ClaimId == x.ClaimBId && c.Subject == subject))
                    .OrderByDescending(x => x.DetectedAtUtc)
                    .Take(Math.Min(10, boundedTake))
                    .Select(
                        x => new WorkspaceContradictionDto(
                            x.ContradictionId,
                            x.ClaimAId,
                            x.ClaimBId,
                            x.Type,
                            x.Severity,
                            x.Status,
                            x.DetectedAtUtc))
                    .ToListAsync(cancellationToken);

                var relationships = await dbContext.MemoryRelationships
                    .AsNoTracking()
                    .Where(x => x.SessionId == companion.SessionId)
                    .Where(x => !(x.FromType == x.ToType && x.FromId == x.ToId))
                    .OrderByDescending(x => x.UpdatedAtUtc)
                    .Take(boundedTake)
                    .Select(
                        x => new WorkspaceRelationshipDto(
                            x.RelationshipId,
                            x.FromType,
                            x.FromId,
                            x.ToType,
                            x.ToId,
                            x.RelationshipType,
                            x.Confidence,
                            x.Strength,
                            x.Status,
                            x.UpdatedAtUtc))
                    .ToListAsync(cancellationToken);

                return Results.Ok(
                    new CompanionMemoryPacketDto(
                        companion.CompanionId,
                        companion.SessionId,
                        claims,
                        contradictions,
                        relationships));
            });

        group.MapGet(
            "/companion/{companionId:guid}/metrics",
            async (HttpContext httpContext, Guid companionId, int? take, MemoryDbContext dbContext, CompanionOwnershipService ownershipService, CancellationToken cancellationToken) =>
            {
                var companion = await ownershipService.ResolveOwnedCompanionAsync(httpContext.User, companionId, dbContext, cancellationToken);
                if (companion is null)
                {
                    return Results.NotFound();
                }

                var boundedTake = Math.Clamp(take ?? 150, 50, 400);
                var sessionToken = companion.SessionId.Trim();
                var sessionPattern = SqlLikePattern.Contains(sessionToken);
                var events = await dbContext.OutboxMessages
                    .AsNoTracking()
                    .Where(x => EF.Functions.ILike(x.PayloadJson, sessionPattern))
                    .OrderByDescending(x => x.OccurredAtUtc)
                    .Take(boundedTake)
                    .Select(
                        x => new WorkspaceEventMetricRow(
                            x.EventType,
                            x.Status,
                            x.RetryCount,
                            x.OccurredAtUtc,
                            x.PublishedAtUtc))
                    .ToListAsync(cancellationToken);

                var toolWindowStart = DateTimeOffset.UtcNow.AddHours(-12);
                var tools = await dbContext.ToolInvocationAudits
                    .AsNoTracking()
                    .Where(x => x.ExecutedAtUtc >= toolWindowStart)
                    .Where(
                        x => EF.Functions.ILike(x.ArgumentsJson, sessionPattern)
                             || EF.Functions.ILike(x.ResultJson, sessionPattern))
                    .OrderByDescending(x => x.ExecutedAtUtc)
                    .Take(300)
                    .ToListAsync(cancellationToken);

                var totalToolCalls = tools.Count;
                var successfulToolCalls = tools.Count(x => x.Succeeded);
                var latencySamples = events
                    .Where(x => x.PublishedAtUtc.HasValue)
                    .Select(x => Math.Max(0, (x.PublishedAtUtc!.Value - x.OccurredAtUtc).TotalSeconds))
                    .ToList();
                var avgLatency = latencySamples.Count == 0
                    ? 0.0
                    : Math.Round(latencySamples.Average(), 3);

                var layerDistribution = await BuildLayerDistributionAsync(companion.CompanionId, companion.SessionId, dbContext, cancellationToken);

                return Results.Ok(
                    new CompanionWorkspaceMetricsDto(
                        companion.CompanionId,
                        companion.SessionId,
                        totalToolCalls,
                        successfulToolCalls,
                        totalToolCalls == 0 ? 100 : Math.Round((successfulToolCalls * 100.0) / totalToolCalls, 1),
                        avgLatency,
                        layerDistribution,
                        events.Take(50).ToArray()));
            });

        group.MapGet(
            "/companion/{companionId:guid}/memory-node-detail",
            async (HttpContext httpContext, Guid companionId, MemoryNodeType nodeType, string nodeId, MemoryDbContext dbContext, CompanionOwnershipService ownershipService, CancellationToken cancellationToken) =>
            {
                var companion = await ownershipService.ResolveOwnedCompanionAsync(httpContext.User, companionId, dbContext, cancellationToken);
                if (companion is null)
                {
                    return Results.NotFound();
                }

                var detail = await ResolveMemoryNodeDetailAsync(companion, nodeType, nodeId, dbContext, cancellationToken);
                return Results.Ok(detail);
            });

        group.MapGet(
            "/companion/{companionId:guid}/memory-timeline",
            async (
                HttpContext httpContext,
                Guid companionId,
                int? page,
                int? pageSize,
                string? kind,
                string? linked,
                string? query,
                MemoryDbContext dbContext,
                CompanionOwnershipService ownershipService,
                CancellationToken cancellationToken) =>
            {
                var companion = await ownershipService.ResolveOwnedCompanionAsync(httpContext.User, companionId, dbContext, cancellationToken);
                if (companion is null)
                {
                    return Results.NotFound();
                }

                var response = await BuildMemoryTimelinePageAsync(
                    companion,
                    Math.Max(1, page ?? 1),
                    Math.Clamp(pageSize ?? 20, 10, 100),
                    kind,
                    linked,
                    query,
                    dbContext,
                    cancellationToken);

                return Results.Ok(response);
            });

        return endpoints;
    }

    private static async Task<DateTimeOffset?> ResolveLastActivityUtcAsync(string sessionId, MemoryDbContext dbContext, CancellationToken cancellationToken)
    {
        var semantic = await dbContext.SemanticClaims
            .AsNoTracking()
            .Where(x => x.Subject == $"session:{sessionId}")
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Select(x => (DateTimeOffset?)x.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var episodic = await dbContext.EpisodicMemoryEvents
            .AsNoTracking()
            .Where(x => x.SessionId == sessionId)
            .OrderByDescending(x => x.OccurredAt)
            .Select(x => (DateTimeOffset?)x.OccurredAt)
            .FirstOrDefaultAsync(cancellationToken);

        var debate = await dbContext.SubconsciousDebateSessions
            .AsNoTracking()
            .Where(x => x.SessionId == sessionId)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Select(x => (DateTimeOffset?)x.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return new[] { semantic, episodic, debate }.Where(x => x.HasValue).Select(x => x!.Value).DefaultIfEmpty().Max();
    }

    private static async Task<IReadOnlyList<WorkspaceLayerMetricDto>> BuildLayerDistributionAsync(
        Guid companionId,
        string sessionId,
        MemoryDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var semantic = await dbContext.SemanticClaims.CountAsync(x => x.Subject == $"session:{sessionId}", cancellationToken);
        var episodic = await dbContext.EpisodicMemoryEvents.CountAsync(x => x.SessionId == sessionId, cancellationToken);
        var procedural = await dbContext.ProceduralRoutines.CountAsync(x => x.CompanionId == companionId, cancellationToken);
        var self = await dbContext.SelfPreferences.CountAsync(x => x.CompanionId == companionId, cancellationToken);

        var raw = new[]
        {
            ("semantic", semantic),
            ("episodic", episodic),
            ("procedural", procedural),
            ("self-model", self),
        };
        var total = Math.Max(1, raw.Sum(x => x.Item2));
        return raw
            .Select(x => new WorkspaceLayerMetricDto(x.Item1, x.Item2, Math.Round((x.Item2 * 100.0) / total, 1)))
            .ToArray();
    }

    private static async Task<WorkspaceMemoryNodeDetailDto> ResolveMemoryNodeDetailAsync(
        CompanionEntity companion,
        MemoryNodeType nodeType,
        string nodeId,
        MemoryDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var trimmedNodeId = nodeId.Trim();
        switch (nodeType)
        {
            case MemoryNodeType.SemanticClaim:
                {
                    if (!Guid.TryParse(trimmedNodeId, out var claimId))
                    {
                        return WorkspaceMemoryNodeDetailDto.NotFound(nodeType, trimmedNodeId);
                    }

                    var claim = await dbContext.SemanticClaims
                        .AsNoTracking()
                        .Where(x => x.CompanionId == companion.CompanionId && x.ClaimId == claimId)
                        .Select(x => new { x.Predicate, x.Value, x.Confidence, x.Status, x.UpdatedAtUtc })
                        .FirstOrDefaultAsync(cancellationToken);
                    return claim is null
                        ? WorkspaceMemoryNodeDetailDto.NotFound(nodeType, trimmedNodeId)
                        : new WorkspaceMemoryNodeDetailDto(
                            true,
                            nodeType,
                            trimmedNodeId,
                            $"Claim: {claim.Predicate}",
                            claim.Value,
                            $"Confidence {claim.Confidence:0.00} · {claim.Status}",
                            claim.UpdatedAtUtc);
                }
            case MemoryNodeType.EpisodicEvent:
                {
                    if (!Guid.TryParse(trimmedNodeId, out var eventId))
                    {
                        return WorkspaceMemoryNodeDetailDto.NotFound(nodeType, trimmedNodeId);
                    }

                    var episodic = await dbContext.EpisodicMemoryEvents
                        .AsNoTracking()
                        .Where(x => x.CompanionId == companion.CompanionId && x.EventId == eventId)
                        .Select(x => new { x.Who, x.What, x.Context, x.OccurredAt })
                        .FirstOrDefaultAsync(cancellationToken);
                    return episodic is null
                        ? WorkspaceMemoryNodeDetailDto.NotFound(nodeType, trimmedNodeId)
                        : new WorkspaceMemoryNodeDetailDto(
                            true,
                            nodeType,
                            trimmedNodeId,
                            $"Episode: {episodic.Who}",
                            episodic.What,
                            episodic.Context,
                            episodic.OccurredAt);
                }
            case MemoryNodeType.ProceduralRoutine:
                {
                    if (!Guid.TryParse(trimmedNodeId, out var routineId))
                    {
                        return WorkspaceMemoryNodeDetailDto.NotFound(nodeType, trimmedNodeId);
                    }

                    var routine = await dbContext.ProceduralRoutines
                        .AsNoTracking()
                        .Where(x => x.CompanionId == companion.CompanionId && x.RoutineId == routineId)
                        .Select(x => new { x.Name, x.Trigger, x.Outcome, x.UpdatedAtUtc })
                        .FirstOrDefaultAsync(cancellationToken);
                    return routine is null
                        ? WorkspaceMemoryNodeDetailDto.NotFound(nodeType, trimmedNodeId)
                        : new WorkspaceMemoryNodeDetailDto(
                            true,
                            nodeType,
                            trimmedNodeId,
                            $"Routine: {routine.Name}",
                            routine.Outcome,
                            $"Trigger: {routine.Trigger}",
                            routine.UpdatedAtUtc);
                }
            case MemoryNodeType.SelfPreference:
                {
                    var preference = await dbContext.SelfPreferences
                        .AsNoTracking()
                        .Where(x => x.CompanionId == companion.CompanionId && x.Key == trimmedNodeId)
                        .Select(x => new { x.Key, x.Value, x.UpdatedAtUtc })
                        .FirstOrDefaultAsync(cancellationToken);
                    return preference is null
                        ? WorkspaceMemoryNodeDetailDto.NotFound(nodeType, trimmedNodeId)
                        : new WorkspaceMemoryNodeDetailDto(
                            true,
                            nodeType,
                            trimmedNodeId,
                            $"Preference: {preference.Key}",
                            preference.Value,
                            null,
                            preference.UpdatedAtUtc);
                }
            case MemoryNodeType.ScheduledAction:
                {
                    if (!Guid.TryParse(trimmedNodeId, out var actionId))
                    {
                        return WorkspaceMemoryNodeDetailDto.NotFound(nodeType, trimmedNodeId);
                    }

                    var action = await dbContext.ScheduledActions
                        .AsNoTracking()
                        .Where(x => x.CompanionId == companion.CompanionId && x.ActionId == actionId)
                        .Select(x => new { x.ActionType, x.InputJson, x.Status, x.UpdatedAtUtc })
                        .FirstOrDefaultAsync(cancellationToken);
                    return action is null
                        ? WorkspaceMemoryNodeDetailDto.NotFound(nodeType, trimmedNodeId)
                        : new WorkspaceMemoryNodeDetailDto(
                            true,
                            nodeType,
                            trimmedNodeId,
                            $"Action: {action.ActionType}",
                            action.InputJson,
                            $"Status: {action.Status}",
                            action.UpdatedAtUtc);
                }
            case MemoryNodeType.SubconsciousDebate:
                {
                    if (!Guid.TryParse(trimmedNodeId, out var debateId))
                    {
                        return WorkspaceMemoryNodeDetailDto.NotFound(nodeType, trimmedNodeId);
                    }

                    var debate = await dbContext.SubconsciousDebateSessions
                        .AsNoTracking()
                        .Where(x => x.CompanionId == companion.CompanionId && x.DebateId == debateId)
                        .Select(x => new { x.TopicKey, x.State, x.TriggerEventType, x.UpdatedAtUtc })
                        .FirstOrDefaultAsync(cancellationToken);
                    return debate is null
                        ? WorkspaceMemoryNodeDetailDto.NotFound(nodeType, trimmedNodeId)
                        : new WorkspaceMemoryNodeDetailDto(
                            true,
                            nodeType,
                            trimmedNodeId,
                            $"Debate: {debate.TopicKey}",
                            $"Trigger: {debate.TriggerEventType}",
                            $"State: {debate.State}",
                            debate.UpdatedAtUtc);
                }
            case MemoryNodeType.ToolInvocation:
                {
                    if (!Guid.TryParse(trimmedNodeId, out var auditId))
                    {
                        return WorkspaceMemoryNodeDetailDto.NotFound(nodeType, trimmedNodeId);
                    }

                    var tool = await dbContext.ToolInvocationAudits
                        .AsNoTracking()
                        .Where(x => x.CompanionId == companion.CompanionId && x.AuditId == auditId)
                        .Select(x => new { x.ToolName, x.ResultJson, x.Succeeded, x.Error, x.ExecutedAtUtc })
                        .FirstOrDefaultAsync(cancellationToken);
                    return tool is null
                        ? WorkspaceMemoryNodeDetailDto.NotFound(nodeType, trimmedNodeId)
                        : new WorkspaceMemoryNodeDetailDto(
                            true,
                            nodeType,
                            trimmedNodeId,
                            $"Tool: {tool.ToolName}",
                            tool.ResultJson,
                            tool.Succeeded ? "Succeeded" : $"Failed: {tool.Error ?? "unknown"}",
                            tool.ExecutedAtUtc);
                }
            default:
                return WorkspaceMemoryNodeDetailDto.NotFound(nodeType, trimmedNodeId);
        }
    }

    private static async Task<WorkspaceMemoryTimelinePageDto> BuildMemoryTimelinePageAsync(
        CompanionEntity companion,
        int page,
        int pageSize,
        string? kind,
        string? linked,
        string? query,
        MemoryDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var sessionSubject = $"session:{companion.SessionId}";
        var normalizedKind = (kind ?? "all").Trim().ToLowerInvariant();
        var normalizedLinked = (linked ?? "all").Trim().ToLowerInvariant();
        var normalizedQuery = (query ?? string.Empty).Trim().ToLowerInvariant();

        var items = new Dictionary<string, TimelineItemBuilder>(StringComparer.OrdinalIgnoreCase);

        var semanticRows = await dbContext.SemanticClaims
            .AsNoTracking()
            .Where(x => x.CompanionId == companion.CompanionId && x.Subject == sessionSubject)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(2000)
            .Select(x => new { x.ClaimId, x.Predicate, x.Value, x.Confidence, x.Status, x.UpdatedAtUtc })
            .ToListAsync(cancellationToken);

        foreach (var row in semanticRows)
        {
            var key = MemoryNodeKey((int)MemoryNodeType.SemanticClaim, row.ClaimId.ToString());
            items[key] = new TimelineItemBuilder(
                key,
                (int)MemoryNodeType.SemanticClaim,
                row.ClaimId.ToString(),
                "semantic",
                row.Predicate,
                row.Value,
                $"Confidence {row.Confidence:0.00} · {row.Status}",
                row.UpdatedAtUtc);
        }

        var episodicRows = await dbContext.EpisodicMemoryEvents
            .AsNoTracking()
            .Where(x => x.CompanionId == companion.CompanionId && x.SessionId == companion.SessionId)
            .OrderByDescending(x => x.OccurredAt)
            .Take(2000)
            .Select(x => new { x.EventId, x.Who, x.What, x.Context, x.OccurredAt })
            .ToListAsync(cancellationToken);

        foreach (var row in episodicRows)
        {
            var key = MemoryNodeKey((int)MemoryNodeType.EpisodicEvent, row.EventId.ToString());
            items[key] = new TimelineItemBuilder(
                key,
                (int)MemoryNodeType.EpisodicEvent,
                row.EventId.ToString(),
                "episodic",
                string.IsNullOrWhiteSpace(row.Who) ? "Episode" : row.Who,
                string.IsNullOrWhiteSpace(row.What) ? row.Context : row.What,
                string.IsNullOrWhiteSpace(row.Context) ? "Session episode" : row.Context,
                row.OccurredAt);
        }

        var selfRows = await dbContext.SelfPreferences
            .AsNoTracking()
            .Where(x => x.CompanionId == companion.CompanionId)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(500)
            .Select(x => new { x.Key, x.Value, x.UpdatedAtUtc })
            .ToListAsync(cancellationToken);

        foreach (var row in selfRows)
        {
            var key = MemoryNodeKey((int)MemoryNodeType.SelfPreference, row.Key);
            items[key] = new TimelineItemBuilder(
                key,
                (int)MemoryNodeType.SelfPreference,
                row.Key,
                "self",
                row.Key,
                row.Value,
                "Companion preference",
                row.UpdatedAtUtc);
        }

        var relationshipRows = await dbContext.MemoryRelationships
            .AsNoTracking()
            .Where(x => x.CompanionId == companion.CompanionId && x.SessionId == companion.SessionId && x.Status == "Active")
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(3000)
            .ToListAsync(cancellationToken);

        foreach (var rel in relationshipRows)
        {
            var fromType = ParseNodeType(rel.FromType);
            var toType = ParseNodeType(rel.ToType);
            var fromKey = MemoryNodeKey((int)fromType, rel.FromId);
            var toKey = MemoryNodeKey((int)toType, rel.ToId);

            if (!items.TryGetValue(fromKey, out var from))
            {
                from = NewFallbackItem(fromType, rel.FromId);
                items[fromKey] = from;
            }

            if (!items.TryGetValue(toKey, out var to))
            {
                to = NewFallbackItem(toType, rel.ToId);
                items[toKey] = to;
            }

            from.Relationships.Add(new WorkspaceMemoryTimelineLinkDto(rel.RelationshipType, "outgoing", to.Title, to.Value, rel.UpdatedAtUtc));
            to.Relationships.Add(new WorkspaceMemoryTimelineLinkDto(rel.RelationshipType, "incoming", from.Title, from.Value, rel.UpdatedAtUtc));
        }

        var all = items.Values
            .Select(x => x.ToDto())
            .Select(x => x with { Relationships = x.Relationships.Take(6).ToArray() })
            .ToList();

        IEnumerable<WorkspaceMemoryTimelineItemDto> filtered = all;
        if (normalizedKind is not "all" and not "")
        {
            filtered = filtered.Where(x => string.Equals(x.Kind, normalizedKind, StringComparison.OrdinalIgnoreCase));
        }

        if (normalizedLinked == "linked")
        {
            filtered = filtered.Where(x => x.Relationships.Count > 0);
        }
        else if (normalizedLinked == "unlinked")
        {
            filtered = filtered.Where(x => x.Relationships.Count == 0);
        }

        if (!string.IsNullOrWhiteSpace(normalizedQuery))
        {
            filtered = filtered.Where(x =>
                $"{x.Title} {x.Value} {x.Meta} {string.Join(' ', x.Relationships.Select(r => $"{r.RelationshipType} {r.PeerLabel} {r.PeerValue}"))}"
                    .ToLowerInvariant()
                    .Contains(normalizedQuery));
        }

        var ordered = filtered
            .OrderByDescending(x => x.UpdatedAtUtc)
            .ToList();

        var total = ordered.Count;
        var skip = Math.Max(0, (page - 1) * pageSize);
        var pageRows = ordered
            .Skip(skip)
            .Take(pageSize)
            .ToArray();

        return new WorkspaceMemoryTimelinePageDto(page, pageSize, total, pageRows);
    }

    private static MemoryNodeType ParseNodeType(string value)
        => Enum.TryParse<MemoryNodeType>(value, true, out var parsed) ? parsed : MemoryNodeType.SemanticClaim;

    private static string MemoryNodeKey(int type, string id)
        => $"{type}:{id}".ToLowerInvariant();

    private static TimelineItemBuilder NewFallbackItem(MemoryNodeType type, string nodeId)
    {
        var kind = type switch
        {
            MemoryNodeType.SemanticClaim => "semantic",
            MemoryNodeType.EpisodicEvent => "episodic",
            MemoryNodeType.ProceduralRoutine => "procedural",
            MemoryNodeType.SelfPreference => "self",
            MemoryNodeType.ScheduledAction => "scheduled",
            MemoryNodeType.SubconsciousDebate => "debate",
            MemoryNodeType.ToolInvocation => "tool",
            _ => "unknown",
        };

        var label = kind switch
        {
            "semantic" => "Claim memory",
            "episodic" => "Episode memory",
            "procedural" => "Routine memory",
            "self" => "Preference memory",
            "scheduled" => "Scheduled memory",
            "debate" => "Debate memory",
            "tool" => "Tool memory",
            _ => "Memory",
        };

        return new TimelineItemBuilder(
            MemoryNodeKey((int)type, nodeId),
            (int)type,
            nodeId,
            kind,
            label,
            nodeId,
            $"Type: {label}",
            DateTimeOffset.UtcNow);
    }

    private sealed class TimelineItemBuilder(
        string key,
        int nodeType,
        string nodeId,
        string kind,
        string title,
        string value,
        string meta,
        DateTimeOffset updatedAtUtc)
    {
        public string Key { get; } = key;
        public int NodeType { get; } = nodeType;
        public string NodeId { get; } = nodeId;
        public string Kind { get; } = kind;
        public string Title { get; } = title;
        public string Value { get; } = value;
        public string Meta { get; } = meta;
        public DateTimeOffset UpdatedAtUtc { get; } = updatedAtUtc;
        public List<WorkspaceMemoryTimelineLinkDto> Relationships { get; } = [];

        public WorkspaceMemoryTimelineItemDto ToDto()
            => new(Key, NodeType, NodeId, Kind, Title, Value, Meta, UpdatedAtUtc, Relationships.ToArray());
    }
}

public sealed record CompanionWorkspaceSummaryDto(
    Guid CompanionId,
    string Name,
    string Tone,
    string Purpose,
    string ModelHint,
    string SessionId,
    string OriginStory,
    DateTimeOffset? BirthDateUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    int SemanticClaimCount,
    int EpisodicEventCount,
    int RelationshipCount,
    int DebateCount,
    WorkspaceDebateSummaryDto? LastDebate,
    DateTimeOffset? LastActivityUtc);

public sealed record WorkspaceDebateSummaryDto(Guid DebateId, string TopicKey, string State, DateTimeOffset UpdatedAtUtc, string? LastError);

public sealed record CompanionMemoryPacketDto(
    Guid CompanionId,
    string SessionId,
    IReadOnlyList<WorkspaceClaimDto> Claims,
    IReadOnlyList<WorkspaceContradictionDto> Contradictions,
    IReadOnlyList<WorkspaceRelationshipDto> Relationships);

public sealed record WorkspaceClaimDto(Guid ClaimId, string Predicate, string Value, double Confidence, string Status, DateTimeOffset UpdatedAtUtc);
public sealed record WorkspaceContradictionDto(Guid ContradictionId, Guid ClaimAId, Guid ClaimBId, string Type, string Severity, string Status, DateTimeOffset DetectedAtUtc);
public sealed record WorkspaceRelationshipDto(Guid RelationshipId, string FromType, string FromId, string ToType, string ToId, string RelationshipType, double Confidence, double Strength, string Status, DateTimeOffset UpdatedAtUtc);

public sealed record CompanionWorkspaceMetricsDto(
    Guid CompanionId,
    string SessionId,
    int TotalToolCalls,
    int SuccessfulToolCalls,
    double ToolSuccessRate,
    double AveragePublishLatencySeconds,
    IReadOnlyList<WorkspaceLayerMetricDto> LayerDistribution,
    IReadOnlyList<WorkspaceEventMetricRow> EventRows);

public sealed record WorkspaceLayerMetricDto(string Layer, int Count, double Percent);
public sealed record WorkspaceEventMetricRow(string EventType, string Status, int RetryCount, DateTimeOffset OccurredAtUtc, DateTimeOffset? PublishedAtUtc);
public sealed record WorkspaceMemoryNodeDetailDto(
    bool Found,
    MemoryNodeType NodeType,
    string NodeId,
    string? Title,
    string? Value,
    string? Summary,
    DateTimeOffset? UpdatedAtUtc)
{
    public static WorkspaceMemoryNodeDetailDto NotFound(MemoryNodeType nodeType, string nodeId)
        => new(false, nodeType, nodeId, null, null, null, null);
}
public sealed record WorkspaceMemoryTimelinePageDto(
    int Page,
    int PageSize,
    int Total,
    IReadOnlyList<WorkspaceMemoryTimelineItemDto> Items);
public sealed record WorkspaceMemoryTimelineItemDto(
    string Key,
    int NodeType,
    string NodeId,
    string Kind,
    string Title,
    string Value,
    string Meta,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<WorkspaceMemoryTimelineLinkDto> Relationships);
public sealed record WorkspaceMemoryTimelineLinkDto(
    string RelationshipType,
    string Direction,
    string PeerLabel,
    string PeerValue,
    DateTimeOffset UpdatedAtUtc);
