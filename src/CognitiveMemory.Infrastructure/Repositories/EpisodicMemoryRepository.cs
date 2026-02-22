using CognitiveMemory.Application.Abstractions;
using CognitiveMemory.Domain.Memory;
using CognitiveMemory.Infrastructure.Events;
using CognitiveMemory.Infrastructure.Persistence;
using CognitiveMemory.Infrastructure.Companions;
using CognitiveMemory.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace CognitiveMemory.Infrastructure.Repositories;

public sealed class EpisodicMemoryRepository(MemoryDbContext dbContext, IOutboxWriter outboxWriter, ICompanionScopeResolver companionScopeResolver) : IEpisodicMemoryRepository
{
    public async Task AppendAsync(EpisodicMemoryEvent memoryEvent, CancellationToken cancellationToken = default)
    {
        var companionId = await companionScopeResolver.ResolveCompanionIdOrThrowAsync(memoryEvent.SessionId, cancellationToken);
        var entity = new EpisodicMemoryEventEntity
        {
            EventId = memoryEvent.EventId,
            CompanionId = companionId,
            SessionId = memoryEvent.SessionId,
            Who = memoryEvent.Who,
            What = memoryEvent.What,
            OccurredAt = memoryEvent.OccurredAt,
            Context = memoryEvent.Context,
            SourceReference = memoryEvent.SourceReference
        };

        dbContext.EpisodicMemoryEvents.Add(entity);
        outboxWriter.Enqueue(
            MemoryEventTypes.EpisodicMemoryCreated,
            aggregateType: "EpisodicMemoryEvent",
            aggregateId: memoryEvent.EventId.ToString("N"),
            payload: new
            {
                companionId,
                memoryEvent.EventId,
                memoryEvent.SessionId,
                memoryEvent.Who,
                memoryEvent.What,
                memoryEvent.Context,
                memoryEvent.OccurredAt,
                memoryEvent.SourceReference
            });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EpisodicMemoryEvent>> QueryBySessionAsync(
        string sessionId,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        var companionId = await companionScopeResolver.ResolveCompanionIdOrThrowAsync(sessionId, cancellationToken);
        var query = dbContext.EpisodicMemoryEvents
            .AsNoTracking()
            .Where(x => x.CompanionId == companionId && x.SessionId == sessionId);

        if (fromUtc.HasValue)
        {
            query = query.Where(x => x.OccurredAt >= fromUtc.Value);
        }

        if (toUtc.HasValue)
        {
            query = query.Where(x => x.OccurredAt <= toUtc.Value);
        }

        var rows = await query
            .OrderByDescending(x => x.OccurredAt)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync(cancellationToken);

        return rows.Select(ToDomain).ToArray();
    }

    public async Task<IReadOnlyList<EpisodicMemoryEvent>> SearchBySessionAsync(
        string sessionId,
        string query,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        var companionId = await companionScopeResolver.ResolveCompanionIdOrThrowAsync(sessionId, cancellationToken);
        if (string.IsNullOrWhiteSpace(query))
        {
            return await QueryBySessionAsync(
                sessionId,
                take: take,
                cancellationToken: cancellationToken);
        }

        var normalizedTerms = query
            .Trim()
            .ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length >= 2)
            .Distinct(StringComparer.Ordinal)
            .Take(8)
            .ToArray();

        IQueryable<EpisodicMemoryEventEntity> queryable = dbContext.EpisodicMemoryEvents
            .AsNoTracking()
            .Where(x => x.CompanionId == companionId && x.SessionId == sessionId);

        foreach (var term in normalizedTerms)
        {
            var current = term;
            queryable = queryable.Where(
                x => x.Who.ToLower().Contains(current)
                     || x.What.ToLower().Contains(current)
                     || x.Context.ToLower().Contains(current)
                     || x.SourceReference.ToLower().Contains(current));
        }

        var rows = await queryable
            .OrderByDescending(x => x.OccurredAt)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync(cancellationToken);

        return rows.Select(ToDomain).ToArray();
    }

    public async Task<IReadOnlyList<EpisodicMemoryEvent>> QueryRangeAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int take = 500,
        CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Companion-scoped episodic range query is required.");
    }

    public async Task<IReadOnlyList<EpisodicMemoryEvent>> QueryRangeAsync(
        Guid companionId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int take = 500,
        CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.EpisodicMemoryEvents
            .AsNoTracking()
            .Where(x => x.CompanionId == companionId && x.OccurredAt >= fromUtc && x.OccurredAt <= toUtc)
            .OrderByDescending(x => x.OccurredAt)
            .Take(Math.Clamp(take, 1, 1000))
            .ToListAsync(cancellationToken);

        return rows.Select(ToDomain).ToArray();
    }

    private static EpisodicMemoryEvent ToDomain(EpisodicMemoryEventEntity x) =>
        new(
            x.EventId,
            x.SessionId,
            x.Who,
            x.What,
            x.OccurredAt,
            x.Context,
            x.SourceReference);
}
