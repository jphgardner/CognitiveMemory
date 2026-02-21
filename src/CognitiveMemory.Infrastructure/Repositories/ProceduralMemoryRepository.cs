using System.Text.Json;
using CognitiveMemory.Application.Abstractions;
using CognitiveMemory.Domain.Memory;
using CognitiveMemory.Infrastructure.Persistence;
using CognitiveMemory.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace CognitiveMemory.Infrastructure.Repositories;

public sealed class ProceduralMemoryRepository(MemoryDbContext dbContext) : IProceduralMemoryRepository
{
    public async Task<ProceduralRoutine> UpsertAsync(ProceduralRoutine routine, CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.ProceduralRoutines.FirstOrDefaultAsync(x => x.RoutineId == routine.RoutineId, cancellationToken);
        var entity = existing ?? new ProceduralRoutineEntity { RoutineId = routine.RoutineId };

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

        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDomain(entity);
    }

    public async Task<IReadOnlyList<ProceduralRoutine>> QueryByTriggerAsync(string trigger, int take = 20, CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.ProceduralRoutines
            .AsNoTracking()
            .Where(x => x.Trigger == trigger)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync(cancellationToken);

        return rows.Select(ToDomain).ToArray();
    }

    public async Task<IReadOnlyList<ProceduralRoutine>> QueryRecentAsync(int take = 50, CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.ProceduralRoutines
            .AsNoTracking()
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(cancellationToken);

        return rows.Select(ToDomain).ToArray();
    }

    public async Task<IReadOnlyList<ProceduralRoutine>> SearchAsync(string query, int take = 20, CancellationToken cancellationToken = default)
    {
        var normalized = query.Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return [];
        }

        IQueryable<ProceduralRoutineEntity> queryable = dbContext.ProceduralRoutines.AsNoTracking();
        if (dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
        {
            var pattern = $"%{normalized}%";
            queryable = queryable.Where(
                x => EF.Functions.ILike(x.Trigger, pattern)
                     || EF.Functions.ILike(x.Name, pattern)
                     || EF.Functions.ILike(x.Outcome, pattern));
        }
        else
        {
            queryable = queryable.Where(
                x => x.Trigger.ToLower().Contains(normalized)
                     || x.Name.ToLower().Contains(normalized)
                     || x.Outcome.ToLower().Contains(normalized));
        }

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
