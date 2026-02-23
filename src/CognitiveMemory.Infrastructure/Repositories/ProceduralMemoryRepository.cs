using System.Text.Json;
using CognitiveMemory.Application.Abstractions;
using CognitiveMemory.Domain.Memory;
using CognitiveMemory.Infrastructure.Events;
using CognitiveMemory.Infrastructure.Persistence;
using CognitiveMemory.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace CognitiveMemory.Infrastructure.Repositories;

public sealed class ProceduralMemoryRepository(MemoryDbContext dbContext, IOutboxWriter outboxWriter) : IProceduralMemoryRepository
{
    public async Task<ProceduralRoutine> UpsertAsync(ProceduralRoutine routine, CancellationToken cancellationToken = default)
        => await UpsertAsync(Guid.Empty, routine, cancellationToken);

    public async Task<ProceduralRoutine> UpsertAsync(Guid companionId, ProceduralRoutine routine, CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.ProceduralRoutines.FirstOrDefaultAsync(x => x.RoutineId == routine.RoutineId, cancellationToken);
        var entity = existing ?? new ProceduralRoutineEntity { RoutineId = routine.RoutineId };

        entity.CompanionId = companionId;
        entity.Trigger = routine.Trigger;
        entity.Name = routine.Name;
        entity.StepsJson = JsonSerializer.Serialize(routine.Steps);
        entity.CheckpointsJson = JsonSerializer.Serialize(routine.Checkpoints);
        entity.Outcome = routine.Outcome;
        entity.CreatedAtUtc = existing?.CreatedAtUtc ?? routine.CreatedAtUtc;
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;

        if (existing is null)
        {
            dbContext.ProceduralRoutines.Add(entity);
        }

        outboxWriter.Enqueue(
            MemoryEventTypes.ProceduralRoutineUpserted,
            aggregateType: "ProceduralRoutine",
            aggregateId: routine.RoutineId.ToString("N"),
            payload: new
            {
                companionId,
                routine.RoutineId,
                routine.Trigger,
                routine.Name,
                stepCount = routine.Steps.Count
            });
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDomain(entity);
    }

    public async Task<IReadOnlyList<ProceduralRoutine>> QueryByTriggerAsync(string trigger, int take = 20, CancellationToken cancellationToken = default)
        => await QueryByTriggerAsync(Guid.Empty, trigger, take, cancellationToken);

    public async Task<IReadOnlyList<ProceduralRoutine>> QueryByTriggerAsync(Guid companionId, string trigger, int take = 20, CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.ProceduralRoutines
            .AsNoTracking()
            .Where(x => x.CompanionId == companionId && x.Trigger == trigger)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync(cancellationToken);

        return rows.Select(ToDomain).ToArray();
    }

    public async Task<IReadOnlyList<ProceduralRoutine>> QueryRecentAsync(int take = 50, CancellationToken cancellationToken = default)
        => await QueryRecentAsync(Guid.Empty, take, cancellationToken);

    public async Task<IReadOnlyList<ProceduralRoutine>> QueryRecentAsync(Guid companionId, int take = 50, CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.ProceduralRoutines
            .AsNoTracking()
            .Where(x => x.CompanionId == companionId)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(cancellationToken);

        return rows.Select(ToDomain).ToArray();
    }

    public async Task<IReadOnlyList<ProceduralRoutine>> SearchAsync(string query, int take = 20, CancellationToken cancellationToken = default)
        => await SearchAsync(Guid.Empty, query, take, cancellationToken);

    public async Task<IReadOnlyList<ProceduralRoutine>> SearchAsync(Guid companionId, string query, int take = 20, CancellationToken cancellationToken = default)
    {
        var normalized = query.Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return [];
        }

        IQueryable<ProceduralRoutineEntity> queryable = dbContext.ProceduralRoutines
            .AsNoTracking()
            .Where(x => x.CompanionId == companionId);
        var pattern = SqlLikePattern.Contains(normalized);
        queryable = queryable.Where(
            x => EF.Functions.ILike(x.Trigger, pattern)
                 || EF.Functions.ILike(x.Name, pattern)
                 || EF.Functions.ILike(x.Outcome, pattern));

        var rows = await queryable
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(cancellationToken);

        return rows.Select(ToDomain).ToArray();
    }

    private static ProceduralRoutine ToDomain(ProceduralRoutineEntity entity) =>
        new(
            entity.RoutineId,
            entity.Trigger,
            entity.Name,
            JsonSerializer.Deserialize<string[]>(entity.StepsJson) ?? [],
            JsonSerializer.Deserialize<string[]>(entity.CheckpointsJson) ?? [],
            entity.Outcome,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc);
}
