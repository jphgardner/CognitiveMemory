using CognitiveMemory.Application.Abstractions;
using CognitiveMemory.Domain.Memory;
using CognitiveMemory.Infrastructure.Events;
using CognitiveMemory.Infrastructure.Persistence;
using CognitiveMemory.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace CognitiveMemory.Infrastructure.Repositories;

public sealed class SelfModelRepository(MemoryDbContext dbContext, IOutboxWriter outboxWriter) : ISelfModelRepository
{
    public async Task<SelfModelSnapshot> GetAsync(CancellationToken cancellationToken = default)
        => await GetAsync(Guid.Empty, cancellationToken);

    public async Task<SelfModelSnapshot> GetAsync(Guid companionId, CancellationToken cancellationToken = default)
    {
        var prefs = await dbContext.SelfPreferences
            .AsNoTracking()
            .Where(x => x.CompanionId == companionId)
            .OrderBy(x => x.Key)
            .Select(x => new SelfPreference(x.Key, x.Value, x.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

        return new SelfModelSnapshot(prefs);
    }

    public async Task SetPreferenceAsync(string key, string value, CancellationToken cancellationToken = default)
        => await SetPreferenceAsync(Guid.Empty, key, value, cancellationToken);

    public async Task SetPreferenceAsync(Guid companionId, string key, string value, CancellationToken cancellationToken = default)
    {
        var normalizedKey = key.Trim();
        var normalizedValue = value.Trim();
        var existing = await dbContext.SelfPreferences.FirstOrDefaultAsync(
            x => x.CompanionId == companionId && x.Key == normalizedKey,
            cancellationToken);
        if (existing is null)
        {
            dbContext.SelfPreferences.Add(
                new SelfPreferenceEntity
                {
                    CompanionId = companionId,
                    Key = normalizedKey,
                    Value = normalizedValue,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                });
        }
        else
        {
            existing.Value = normalizedValue;
            existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        outboxWriter.Enqueue(
            MemoryEventTypes.SelfPreferenceSet,
            aggregateType: "SelfPreference",
            aggregateId: normalizedKey,
            payload: new
            {
                companionId,
                key = normalizedKey,
                value = normalizedValue
            });
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
