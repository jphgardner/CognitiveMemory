using CognitiveMemory.Application.AI.Tooling;
using CognitiveMemory.Domain.Entities;
using CognitiveMemory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CognitiveMemory.Infrastructure.Repositories;

public sealed class ClaimCalibrationRepository(MemoryDbContext dbContext) : IClaimCalibrationRepository
{
    public async Task AddAsync(ClaimCalibrationRecord record, CancellationToken cancellationToken)
    {
        dbContext.ClaimCalibrations.Add(new ClaimCalibration
        {
            CalibrationId = Guid.NewGuid(),
            ClaimId = record.ClaimId,
            RecommendedConfidence = Math.Clamp(record.RecommendedConfidence, 0, 1),
            SourceEventRef = record.SourceEventRef,
            ReasonCodesJson = record.ReasonCodesJson,
            CreatedAt = record.CreatedAt
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, ClaimCalibrationRecord>> GetLatestByClaimIdsAsync(IReadOnlyCollection<Guid> claimIds, CancellationToken cancellationToken)
    {
        if (claimIds.Count == 0)
        {
            return new Dictionary<Guid, ClaimCalibrationRecord>();
        }

        var rows = await dbContext.ClaimCalibrations
            .AsNoTracking()
            .Where(x => claimIds.Contains(x.ClaimId))
            .GroupBy(x => x.ClaimId)
            .Select(g => g.OrderByDescending(x => x.CreatedAt).First())
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(
            x => x.ClaimId,
            x => new ClaimCalibrationRecord
            {
                ClaimId = x.ClaimId,
                RecommendedConfidence = x.RecommendedConfidence,
                SourceEventRef = x.SourceEventRef,
                ReasonCodesJson = x.ReasonCodesJson,
                CreatedAt = x.CreatedAt
            });
    }

    public async Task<IReadOnlyList<ClaimCalibrationRecord>> GetRecentAsync(int take, CancellationToken cancellationToken)
    {
        var rows = await dbContext.ClaimCalibrations
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync(cancellationToken);

        return rows.Select(x => new ClaimCalibrationRecord
        {
            ClaimId = x.ClaimId,
            RecommendedConfidence = x.RecommendedConfidence,
            SourceEventRef = x.SourceEventRef,
            ReasonCodesJson = x.ReasonCodesJson,
            CreatedAt = x.CreatedAt
        }).ToList();
    }

    public async Task<CalibrationTrendSummary> GetTrendSummaryAsync(TimeSpan window, CancellationToken cancellationToken)
    {
        var floor = DateTimeOffset.UtcNow - (window <= TimeSpan.Zero ? TimeSpan.FromHours(24) : window);
        var rows = await dbContext.ClaimCalibrations
            .AsNoTracking()
            .Where(x => x.CreatedAt >= floor)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return new CalibrationTrendSummary
            {
                WindowCount = 0,
                DistinctClaims = 0,
                AverageRecommendedConfidence = 0,
                MinRecommendedConfidence = 0,
                MaxRecommendedConfidence = 0
            };
        }

        return new CalibrationTrendSummary
        {
            WindowCount = rows.Count,
            DistinctClaims = rows.Select(x => x.ClaimId).Distinct().Count(),
            AverageRecommendedConfidence = rows.Average(x => x.RecommendedConfidence),
            MinRecommendedConfidence = rows.Min(x => x.RecommendedConfidence),
            MaxRecommendedConfidence = rows.Max(x => x.RecommendedConfidence)
        };
    }
}
