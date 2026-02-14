using CognitiveMemory.Application.AI.Tooling;
using CognitiveMemory.Domain.Entities;
using CognitiveMemory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CognitiveMemory.Infrastructure.Repositories;

public sealed class PolicyDecisionRepository(MemoryDbContext dbContext) : IPolicyDecisionRepository
{
    public async Task<Guid> SaveAsync(PolicyDecisionWriteRequest request, CancellationToken cancellationToken)
    {
        var row = new PolicyDecision
        {
            DecisionId = Guid.NewGuid(),
            SourceType = request.SourceType,
            SourceRef = request.SourceRef,
            Decision = request.Decision,
            RiskScore = request.RiskScore,
            PolicyVersion = request.PolicyVersion,
            ReasonCodesJson = JsonStringArrayCodec.Serialize(request.ReasonCodes),
            MetadataJson = request.MetadataJson,
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.PolicyDecisions.Add(row);
        await dbContext.SaveChangesAsync(cancellationToken);

        return row.DecisionId;
    }

    public async Task<IReadOnlyList<PolicyDecisionRecord>> GetBySourceAsync(string sourceType, string sourceRef, CancellationToken cancellationToken)
    {
        var rows = await dbContext.PolicyDecisions
            .AsNoTracking()
            .Where(x => x.SourceType == sourceType && x.SourceRef == sourceRef)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        return rows.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<PolicyDecisionRecord>> GetRecentAsync(int take, CancellationToken cancellationToken)
    {
        var rows = await dbContext.PolicyDecisions
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(cancellationToken);

        return rows.Select(Map).ToList();
    }

    private static PolicyDecisionRecord Map(PolicyDecision row)
    {
        return new PolicyDecisionRecord
        {
            DecisionId = row.DecisionId,
            SourceType = row.SourceType,
            SourceRef = row.SourceRef,
            Decision = row.Decision,
            RiskScore = row.RiskScore,
            PolicyVersion = row.PolicyVersion,
            ReasonCodes = JsonStringArrayCodec.DeserializeOrEmpty(row.ReasonCodesJson),
            MetadataJson = row.MetadataJson,
            CreatedAt = row.CreatedAt
        };
    }
}
