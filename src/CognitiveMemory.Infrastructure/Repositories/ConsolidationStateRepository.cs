using CognitiveMemory.Application.Abstractions;
using CognitiveMemory.Infrastructure.Persistence;
using CognitiveMemory.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace CognitiveMemory.Infrastructure.Repositories;

public sealed class ConsolidationStateRepository(MemoryDbContext dbContext) : IConsolidationStateRepository
{
    public Task<bool> IsProcessedAsync(Guid episodicEventId, CancellationToken cancellationToken = default)
        => dbContext.ConsolidationPromotions
            .AsNoTracking()
            .AnyAsync(x => x.EpisodicEventId == episodicEventId, cancellationToken);

    public async Task MarkProcessedAsync(
        Guid episodicEventId,
        string outcome,
        Guid? semanticClaimId = null,
        string? notes = null,
        CancellationToken cancellationToken = default)
    {
        var exists = await dbContext.ConsolidationPromotions
            .AsNoTracking()
            .AnyAsync(x => x.EpisodicEventId == episodicEventId, cancellationToken);

        if (exists)
        {
            return;
        }

        dbContext.ConsolidationPromotions.Add(
            new ConsolidationPromotionEntity
            {
                PromotionId = Guid.NewGuid(),
                EpisodicEventId = episodicEventId,
                SemanticClaimId = semanticClaimId,
                Outcome = outcome,
                Notes = notes,
                ProcessedAtUtc = DateTimeOffset.UtcNow
            });

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
