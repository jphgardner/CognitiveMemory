using System;
using System.Collections.Generic;
using System.Linq;
using CognitiveMemory.Application.Interfaces;
using CognitiveMemory.Domain.Entities;
using CognitiveMemory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CognitiveMemory.Infrastructure.Repositories;

public sealed class EntityRepository(MemoryDbContext dbContext) : IEntityRepository
{
    public async Task<IReadOnlyList<MemoryEntity>> GetRecentAsync(int take, CancellationToken cancellationToken)
    {
        var size = Math.Clamp(take, 1, 200);
        return await dbContext.Entities
            .OrderByDescending(e => e.UpdatedAt)
            .ThenBy(e => e.Name)
            .Take(size)
            .ToListAsync(cancellationToken);
    }

    public Task<MemoryEntity?> GetByIdAsync(Guid entityId, CancellationToken cancellationToken)
    {
        return dbContext.Entities.FirstOrDefaultAsync(e => e.EntityId == entityId, cancellationToken);
    }

    public async Task<MemoryEntity> UpsertAsync(
        Guid entityId,
        string type,
        string name,
        IEnumerable<string>? aliases,
        string metadata,
        CancellationToken cancellationToken)
    {
        var normalizedType = NormalizeType(type);
        var normalizedName = NormalizeName(name);
        var normalizedAliases = NormalizeAliases(aliases, normalizedName);
        var normalizedMetadata = string.IsNullOrWhiteSpace(metadata) ? "{}" : metadata;
        var now = DateTimeOffset.UtcNow;

        var existing = await dbContext.Entities.FirstOrDefaultAsync(e => e.EntityId == entityId, cancellationToken);
        if (existing is null)
        {
            var created = new MemoryEntity
            {
                EntityId = entityId,
                Type = normalizedType,
                Name = normalizedName,
                Aliases = normalizedAliases,
                Metadata = normalizedMetadata,
                CreatedAt = now,
                UpdatedAt = now
            };

            dbContext.Entities.Add(created);
            await SaveWithCollisionHandlingAsync(created, normalizedName, cancellationToken);
            return created;
        }

        var changed = false;
        if (!string.Equals(existing.Type, normalizedType, StringComparison.Ordinal))
        {
            existing.Type = normalizedType;
            changed = true;
        }

        if (!string.Equals(existing.Name, normalizedName, StringComparison.Ordinal))
        {
            existing.Name = normalizedName;
            changed = true;
        }

        if (!existing.Aliases.SequenceEqual(normalizedAliases, StringComparer.Ordinal))
        {
            existing.Aliases = normalizedAliases;
            changed = true;
        }

        if (!string.Equals(existing.Metadata, normalizedMetadata, StringComparison.Ordinal))
        {
            existing.Metadata = normalizedMetadata;
            changed = true;
        }

        if (changed)
        {
            existing.UpdatedAt = now;
            await SaveWithCollisionHandlingAsync(existing, normalizedName, cancellationToken);
        }

        return existing;
    }

    private static string NormalizeType(string value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "Concept" : value.Trim();
        return normalized.Length <= 32 ? normalized : normalized[..32];
    }

    private static string NormalizeName(string value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "Unknown" : value.Trim();
        return normalized.Length <= 256 ? normalized : normalized[..256];
    }

    private static List<string> NormalizeAliases(IEnumerable<string>? aliases, string name)
    {
        return (aliases ?? [])
            .Select(a => (a ?? string.Empty).Trim())
            .Where(a => a.Length > 0 && !string.Equals(a, name, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(32)
            .ToList();
    }

    private async Task SaveWithCollisionHandlingAsync(
        MemoryEntity entity,
        string originalName,
        CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            var fallbackName = BuildCollisionSafeName(originalName, entity.EntityId);
            if (!entity.Aliases.Any(a => string.Equals(a, originalName, StringComparison.OrdinalIgnoreCase)))
            {
                entity.Aliases.Insert(0, originalName);
            }

            entity.Name = fallbackName;
            entity.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private static string BuildCollisionSafeName(string originalName, Guid entityId)
    {
        var suffix = entityId.ToString("N")[..8];
        var trimmed = originalName.Length > 240 ? originalName[..240] : originalName;
        return $"{trimmed}#{suffix}";
    }
}
