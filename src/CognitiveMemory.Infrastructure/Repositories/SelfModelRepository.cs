using CognitiveMemory.Application.Abstractions;
using CognitiveMemory.Domain.Memory;
using CognitiveMemory.Infrastructure.Persistence;
using CognitiveMemory.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace CognitiveMemory.Infrastructure.Repositories;

public sealed class SelfModelRepository(MemoryDbContext dbContext) : ISelfModelRepository
{
    public async Task<SelfModelSnapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        var prefs = await dbContext.SelfPreferences
            .AsNoTracking()
            .OrderBy(x => x.Key)
            .Select(x => new SelfPreference(x.Key, x.Value, x.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

        return new SelfModelSnapshot(prefs);
    }

    public async Task SetPreferenceAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.SelfPreferences.FirstOrDefaultAsync(x => x.Key == key, cancellationToken);
        if (existing is null)
        {
            dbContext.SelfPreferences.Add(
                new SelfPreferenceEntity
                {
                    Key = key,
                    Value = value,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                });
        }
        else
        {
            existing.Value = value;
            existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
