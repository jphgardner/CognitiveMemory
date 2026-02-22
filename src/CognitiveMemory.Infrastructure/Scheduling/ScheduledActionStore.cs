using CognitiveMemory.Infrastructure.Events;
using CognitiveMemory.Infrastructure.Companions;
using CognitiveMemory.Infrastructure.Persistence;
using CognitiveMemory.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace CognitiveMemory.Infrastructure.Scheduling;

public sealed class ScheduledActionStore(
    MemoryDbContext dbContext,
    ICompanionScopeResolver companionScopeResolver,
    IOutboxWriter outboxWriter) : IScheduledActionStore
{
    public async Task<ScheduledActionEntity> ScheduleAsync(
        string sessionId,
        string actionType,
        string inputJson,
        DateTimeOffset runAtUtc,
        int maxAttempts,
        CancellationToken cancellationToken = default)
    {
        var companionId = await companionScopeResolver.ResolveCompanionIdOrThrowAsync(sessionId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var entity = new ScheduledActionEntity
        {
            ActionId = Guid.NewGuid(),
            CompanionId = companionId,
            SessionId = sessionId.Trim(),
            ActionType = actionType.Trim(),
            InputJson = string.IsNullOrWhiteSpace(inputJson) ? "{}" : inputJson.Trim(),
            RunAtUtc = runAtUtc,
            Status = ScheduledActionStatus.Pending,
            Attempts = 0,
            MaxAttempts = Math.Max(1, maxAttempts),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        dbContext.ScheduledActions.Add(entity);
        outboxWriter.Enqueue(
            MemoryEventTypes.ScheduledActionCreated,
            aggregateType: "ScheduledAction",
            aggregateId: entity.ActionId.ToString("N"),
            payload: new
            {
                companionId,
                entity.ActionId,
                entity.SessionId,
                entity.ActionType,
                entity.RunAtUtc,
                entity.MaxAttempts
            });
        await dbContext.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task<IReadOnlyList<ScheduledActionEntity>> ListAsync(
        string? sessionId,
        string? status,
        int take,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.ScheduledActions.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            var normalizedSessionId = sessionId.Trim();
            var companionId = await companionScopeResolver.ResolveCompanionIdOrThrowAsync(normalizedSessionId, cancellationToken);
            query = query.Where(x => x.CompanionId == companionId && x.SessionId == normalizedSessionId);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalizedStatus = status.Trim();
            query = query.Where(x => x.Status == normalizedStatus);
        }

        return await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> CancelAsync(Guid actionId, CancellationToken cancellationToken = default)
    {
        var row = await dbContext.ScheduledActions.FirstOrDefaultAsync(x => x.ActionId == actionId, cancellationToken);
        if (row is null)
        {
            return false;
        }

        if (row.Status is ScheduledActionStatus.Completed or ScheduledActionStatus.Canceled)
        {
            return false;
        }

        row.Status = ScheduledActionStatus.Canceled;
        row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        row.CompletedAtUtc = DateTimeOffset.UtcNow;
        outboxWriter.Enqueue(
            MemoryEventTypes.ScheduledActionCanceled,
            aggregateType: "ScheduledAction",
            aggregateId: row.ActionId.ToString("N"),
            payload: new { row.CompanionId, row.ActionId, row.SessionId, row.ActionType });
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<ScheduledActionEntity>> ClaimDueAsync(
        DateTimeOffset nowUtc,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.ScheduledActions
            .Where(x => x.Status == ScheduledActionStatus.Pending && x.RunAtUtc <= nowUtc)
            .OrderBy(x => x.RunAtUtc)
            .Take(Math.Clamp(batchSize, 1, 200))
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return rows;
        }

        foreach (var row in rows)
        {
            row.Status = ScheduledActionStatus.Running;
            row.Attempts += 1;
            row.UpdatedAtUtc = nowUtc;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return rows;
    }

    public async Task MarkCompletedAsync(Guid actionId, CancellationToken cancellationToken = default)
    {
        var row = await dbContext.ScheduledActions.FirstOrDefaultAsync(x => x.ActionId == actionId, cancellationToken);
        if (row is null)
        {
            return;
        }

        row.Status = ScheduledActionStatus.Completed;
        row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        row.CompletedAtUtc = row.UpdatedAtUtc;
        row.LastError = null;
        outboxWriter.Enqueue(
            MemoryEventTypes.ScheduledActionExecuted,
            aggregateType: "ScheduledAction",
            aggregateId: row.ActionId.ToString("N"),
            payload: new { row.CompanionId, row.ActionId, row.SessionId, row.ActionType, row.Attempts });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkFailedAsync(Guid actionId, string error, bool exhaustedRetries, CancellationToken cancellationToken = default)
    {
        var row = await dbContext.ScheduledActions.FirstOrDefaultAsync(x => x.ActionId == actionId, cancellationToken);
        if (row is null)
        {
            return;
        }

        row.LastError = Truncate(error, 1000);
        row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        row.Status = exhaustedRetries ? ScheduledActionStatus.Failed : ScheduledActionStatus.Pending;
        if (exhaustedRetries)
        {
            row.CompletedAtUtc = row.UpdatedAtUtc;
        }

        outboxWriter.Enqueue(
            exhaustedRetries ? MemoryEventTypes.ScheduledActionFailed : MemoryEventTypes.ScheduledActionRetrying,
            aggregateType: "ScheduledAction",
            aggregateId: row.ActionId.ToString("N"),
            payload: new
            {
                row.CompanionId,
                row.ActionId,
                row.SessionId,
                row.ActionType,
                row.Attempts,
                row.MaxAttempts,
                error = row.LastError
            });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
