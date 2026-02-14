using CognitiveMemory.Application.AI.Tooling;
using CognitiveMemory.Domain.Entities;
using CognitiveMemory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CognitiveMemory.Infrastructure.Repositories;

public sealed class OutboxRepository(MemoryDbContext dbContext) : IOutboxRepository
{
    public async Task<Guid> EnqueueAsync(OutboxEventWriteRequest request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existing = await dbContext.OutboxEvents
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.EventType == request.EventType && x.IdempotencyKey == request.IdempotencyKey,
                    cancellationToken);
            if (existing is not null)
            {
                return existing.EventId;
            }
        }

        var row = new OutboxEvent
        {
            EventId = Guid.NewGuid(),
            EventType = request.EventType,
            AggregateType = request.AggregateType,
            AggregateId = request.AggregateId,
            PayloadJson = request.PayloadJson,
            IdempotencyKey = request.IdempotencyKey,
            Status = OutboxStatuses.Pending,
            Attempts = 0,
            CreatedAt = DateTimeOffset.UtcNow,
            AvailableAt = request.AvailableAt ?? DateTimeOffset.UtcNow
        };

        dbContext.OutboxEvents.Add(row);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
            {
                var conflict = await dbContext.OutboxEvents
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        x => x.EventType == request.EventType && x.IdempotencyKey == request.IdempotencyKey,
                        cancellationToken);
                if (conflict is not null)
                {
                    return conflict.EventId;
                }
            }

            throw;
        }

        return row.EventId;
    }

    public async Task<IReadOnlyList<OutboxEventRecord>> ReservePendingAsync(int batchSize, TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var maxBatch = Math.Clamp(batchSize, 1, 200);

        var rows = await dbContext.OutboxEvents
            .Where(x =>
                ((x.Status == OutboxStatuses.Pending || x.Status == OutboxStatuses.Failed) && x.AvailableAt <= now) ||
                (x.Status == OutboxStatuses.Processing && x.LockedUntil < now))
            .OrderBy(x => x.CreatedAt)
            .Take(maxBatch)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return [];
        }

        foreach (var row in rows)
        {
            row.Status = OutboxStatuses.Processing;
            row.Attempts += 1;
            row.LockedUntil = now.Add(leaseDuration);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return rows
            .Select(x => new OutboxEventRecord
            {
                EventId = x.EventId,
                EventType = x.EventType,
                AggregateType = x.AggregateType,
                AggregateId = x.AggregateId,
                PayloadJson = x.PayloadJson,
                IdempotencyKey = x.IdempotencyKey,
                Attempts = x.Attempts,
                CreatedAt = x.CreatedAt
            })
            .ToList();
    }

    public async Task MarkSucceededAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var row = await dbContext.OutboxEvents.FirstOrDefaultAsync(x => x.EventId == eventId, cancellationToken);
        if (row is null)
        {
            return;
        }

        row.Status = OutboxStatuses.Succeeded;
        row.ProcessedAt = DateTimeOffset.UtcNow;
        row.LockedUntil = null;
        row.LastError = null;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkFailedAsync(Guid eventId, string error, TimeSpan retryDelay, CancellationToken cancellationToken)
    {
        var row = await dbContext.OutboxEvents.FirstOrDefaultAsync(x => x.EventId == eventId, cancellationToken);
        if (row is null)
        {
            return;
        }

        row.Status = OutboxStatuses.Failed;
        row.LockedUntil = null;
        row.AvailableAt = DateTimeOffset.UtcNow.Add(retryDelay);
        row.LastError = error;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<OutboxStatusSummary> GetStatusSummaryAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var groupedCounts = await dbContext.OutboxEvents
            .AsNoTracking()
            .GroupBy(x => x.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var total = groupedCounts.Sum(x => x.Count);
        var pending = groupedCounts.Where(x => x.Key == OutboxStatuses.Pending).Select(x => x.Count).FirstOrDefault();
        var failed = groupedCounts.Where(x => x.Key == OutboxStatuses.Failed).Select(x => x.Count).FirstOrDefault();
        var processing = groupedCounts.Where(x => x.Key == OutboxStatuses.Processing).Select(x => x.Count).FirstOrDefault();
        var succeeded = groupedCounts.Where(x => x.Key == OutboxStatuses.Succeeded).Select(x => x.Count).FirstOrDefault();

        var staleProcessing = await dbContext.OutboxEvents
            .AsNoTracking()
            .CountAsync(
                x => x.Status == OutboxStatuses.Processing && x.LockedUntil < now,
                cancellationToken);

        var oldestPendingCreatedAt = await dbContext.OutboxEvents
            .AsNoTracking()
            .Where(x => x.Status == OutboxStatuses.Pending || x.Status == OutboxStatuses.Failed)
            .OrderBy(x => x.CreatedAt)
            .Select(x => (DateTimeOffset?)x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return new OutboxStatusSummary
        {
            Total = total,
            Pending = pending,
            Failed = failed,
            Processing = processing,
            Succeeded = succeeded,
            StaleProcessing = staleProcessing,
            OldestPendingAgeSeconds = oldestPendingCreatedAt.HasValue ? Math.Max(0, (now - oldestPendingCreatedAt.Value).TotalSeconds) : 0
        };
    }

    public async Task<IReadOnlyList<OutboxEventStatusRecord>> GetRecentAsync(int take, string? status, CancellationToken cancellationToken)
    {
        var boundedTake = Math.Clamp(take, 1, 200);
        var query = dbContext.OutboxEvents
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(x => x.Status == status);
        }

        var rows = await query
            .OrderByDescending(x => x.CreatedAt)
            .Take(boundedTake)
            .ToListAsync(cancellationToken);

        return rows.Select(x => new OutboxEventStatusRecord
        {
            EventId = x.EventId,
            EventType = x.EventType,
            AggregateType = x.AggregateType,
            AggregateId = x.AggregateId,
            Status = x.Status,
            Attempts = x.Attempts,
            CreatedAt = x.CreatedAt,
            AvailableAt = x.AvailableAt,
            LockedUntil = x.LockedUntil,
            ProcessedAt = x.ProcessedAt,
            LastError = x.LastError
        }).ToList();
    }
}
