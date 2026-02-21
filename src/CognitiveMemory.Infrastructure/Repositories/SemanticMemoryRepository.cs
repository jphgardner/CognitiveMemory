using CognitiveMemory.Application.Abstractions;
using CognitiveMemory.Domain.Memory;
using CognitiveMemory.Infrastructure.Persistence;
using CognitiveMemory.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace CognitiveMemory.Infrastructure.Repositories;

public sealed class SemanticMemoryRepository(MemoryDbContext dbContext) : ISemanticMemoryRepository
{
    public async Task<SemanticClaim> CreateClaimAsync(SemanticClaim claim, CancellationToken cancellationToken = default)
    {
        var entity = new SemanticClaimEntity
        {
            ClaimId = claim.ClaimId,
            Subject = claim.Subject,
            Predicate = claim.Predicate,
            Value = claim.Value,
            Confidence = claim.Confidence,
            Scope = claim.Scope,
            Status = claim.Status.ToString(),
            ValidFromUtc = claim.ValidFromUtc,
            ValidToUtc = claim.ValidToUtc,
            SupersededByClaimId = claim.SupersededByClaimId,
            CreatedAtUtc = claim.CreatedAtUtc,
            UpdatedAtUtc = claim.UpdatedAtUtc
        };

        dbContext.SemanticClaims.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return claim;
    }

    public async Task<SemanticClaim?> GetByIdAsync(Guid claimId, CancellationToken cancellationToken = default)
    {
        var row = await dbContext.SemanticClaims.AsNoTracking().FirstOrDefaultAsync(x => x.ClaimId == claimId, cancellationToken);
        return row is null ? null : ToDomain(row);
    }

    public async Task SupersedeAsync(Guid claimId, Guid supersededByClaimId, CancellationToken cancellationToken = default)
    {
        var row = await dbContext.SemanticClaims.FirstOrDefaultAsync(x => x.ClaimId == claimId, cancellationToken)
                  ?? throw new InvalidOperationException("Claim not found.");
        row.Status = SemanticClaimStatus.Superseded.ToString();
        row.SupersededByClaimId = supersededByClaimId;
        row.ValidToUtc = DateTimeOffset.UtcNow;
        row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<ClaimEvidence> AddEvidenceAsync(ClaimEvidence evidence, CancellationToken cancellationToken = default)
    {
        var entity = new ClaimEvidenceEntity
        {
            EvidenceId = evidence.EvidenceId,
            ClaimId = evidence.ClaimId,
            SourceType = evidence.SourceType,
            SourceReference = evidence.SourceReference,
            ExcerptOrSummary = evidence.ExcerptOrSummary,
            Strength = evidence.Strength,
            CapturedAtUtc = evidence.CapturedAtUtc
        };

        dbContext.ClaimEvidence.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return evidence;
    }

    public async Task<ClaimContradiction> AddContradictionAsync(ClaimContradiction contradiction, CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.ClaimContradictions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => (x.ClaimAId == contradiction.ClaimAId && x.ClaimBId == contradiction.ClaimBId)
                     || (x.ClaimAId == contradiction.ClaimBId && x.ClaimBId == contradiction.ClaimAId),
                cancellationToken);

        if (existing is not null)
        {
            return new ClaimContradiction(
                existing.ContradictionId,
                existing.ClaimAId,
                existing.ClaimBId,
                existing.Type,
                existing.Severity,
                existing.DetectedAtUtc,
                existing.Status);
        }

        var entity = new ClaimContradictionEntity
        {
            ContradictionId = contradiction.ContradictionId,
            ClaimAId = contradiction.ClaimAId,
            ClaimBId = contradiction.ClaimBId,
            Type = contradiction.Type,
            Severity = contradiction.Severity,
            DetectedAtUtc = contradiction.DetectedAtUtc,
            Status = contradiction.Status
        };

        dbContext.ClaimContradictions.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return contradiction;
    }

    public async Task<IReadOnlyList<SemanticClaim>> QueryClaimsAsync(
        string? subject = null,
        string? predicate = null,
        SemanticClaimStatus? status = null,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.SemanticClaims.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(subject))
        {
            var normalizedSubject = subject.Trim().ToLowerInvariant();
            query = query.Where(x => x.Subject.ToLower().Contains(normalizedSubject));
        }

        if (!string.IsNullOrWhiteSpace(predicate))
        {
            var normalizedPredicate = predicate.Trim().ToLowerInvariant();
            query = query.Where(x => x.Predicate.ToLower().Contains(normalizedPredicate));
        }

        if (status.HasValue)
        {
            query = query.Where(x => x.Status == status.Value.ToString());
        }

        var rows = await query
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync(cancellationToken);

        return rows.Select(
                ToDomain)
            .ToArray();
    }

    public async Task<IReadOnlyList<SemanticClaim>> SearchClaimsAsync(
        string query,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return await QueryClaimsAsync(take: take, cancellationToken: cancellationToken);
        }

        var normalizedTerms = query
            .Trim()
            .ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length >= 2)
            .Distinct(StringComparer.Ordinal)
            .Take(10)
            .ToArray();

        IQueryable<SemanticClaimEntity> queryable = dbContext.SemanticClaims.AsNoTracking();
        foreach (var term in normalizedTerms)
        {
            var current = term;
            queryable = queryable.Where(
                x => x.Subject.ToLower().Contains(current)
                     || x.Predicate.ToLower().Contains(current)
                     || x.Value.ToLower().Contains(current)
                     || x.Scope.ToLower().Contains(current));
        }

        var rows = await queryable
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync(cancellationToken);

        return rows.Select(ToDomain).ToArray();
    }

    public async Task<int> DecayActiveClaimsAsync(DateTimeOffset staleBeforeUtc, double decayStep, double minConfidence, CancellationToken cancellationToken = default)
    {
        var claims = await dbContext.SemanticClaims
            .Where(x => x.Status == SemanticClaimStatus.Active.ToString() && x.UpdatedAtUtc <= staleBeforeUtc)
            .ToListAsync(cancellationToken);

        foreach (var claim in claims)
        {
            claim.Confidence = Math.Max(0, claim.Confidence - decayStep);
            claim.UpdatedAtUtc = DateTimeOffset.UtcNow;
            if (claim.Confidence < minConfidence)
            {
                claim.Status = SemanticClaimStatus.Retracted.ToString();
                claim.ValidToUtc = DateTimeOffset.UtcNow;
            }
        }

        if (claims.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return claims.Count;
    }

    private static SemanticClaim ToDomain(SemanticClaimEntity x) =>
        new(
            x.ClaimId,
            x.Subject,
            x.Predicate,
            x.Value,
            x.Confidence,
            x.Scope,
            Enum.TryParse<SemanticClaimStatus>(x.Status, true, out var parsed) ? parsed : SemanticClaimStatus.Active,
            x.ValidFromUtc,
            x.ValidToUtc,
            x.SupersededByClaimId,
            x.CreatedAtUtc,
            x.UpdatedAtUtc);
}
