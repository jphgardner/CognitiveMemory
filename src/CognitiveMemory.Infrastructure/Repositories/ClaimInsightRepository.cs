using CognitiveMemory.Application.AI.Tooling;
using CognitiveMemory.Domain.Entities;
using CognitiveMemory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CognitiveMemory.Infrastructure.Repositories;

public sealed class ClaimInsightRepository(MemoryDbContext dbContext) : IClaimInsightRepository
{
    public async Task UpsertAsync(ClaimInsightRecord record, CancellationToken cancellationToken)
    {
        var existing = await dbContext.ClaimInsights.FirstOrDefaultAsync(x => x.ClaimId == record.ClaimId, cancellationToken);
        if (existing is null)
        {
            dbContext.ClaimInsights.Add(new ClaimInsight
            {
                ClaimId = record.ClaimId,
                Summary = record.Summary,
                KeywordsJson = JsonStringArrayCodec.Serialize(record.Keywords),
                SourceEventRef = record.SourceEventRef,
                UpdatedAt = record.UpdatedAt
            });
        }
        else
        {
            existing.Summary = record.Summary;
            existing.KeywordsJson = JsonStringArrayCodec.Serialize(record.Keywords);
            existing.SourceEventRef = record.SourceEventRef;
            existing.UpdatedAt = record.UpdatedAt;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, ClaimInsightRecord>> GetByClaimIdsAsync(IReadOnlyCollection<Guid> claimIds, CancellationToken cancellationToken)
    {
        if (claimIds.Count == 0)
        {
            return new Dictionary<Guid, ClaimInsightRecord>();
        }

        var rows = await dbContext.ClaimInsights
            .AsNoTracking()
            .Where(x => claimIds.Contains(x.ClaimId))
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(x => x.ClaimId, Map);
    }

    private static ClaimInsightRecord Map(ClaimInsight row)
    {
        return new ClaimInsightRecord
        {
            ClaimId = row.ClaimId,
            Summary = row.Summary,
            Keywords = JsonStringArrayCodec.DeserializeOrEmpty(row.KeywordsJson),
            SourceEventRef = row.SourceEventRef,
            UpdatedAt = row.UpdatedAt
        };
    }
}
