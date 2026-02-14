using CognitiveMemory.Application.Contracts;
using CognitiveMemory.Application.Interfaces;
using CognitiveMemory.Domain.Entities;
using CognitiveMemory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CognitiveMemory.Infrastructure.Repositories;

public class ClaimRepository(MemoryDbContext dbContext) : IClaimRepository
{
    public async Task<IReadOnlyList<ClaimListItem>> GetRecentAsync(int take, CancellationToken cancellationToken)
    {
        return await dbContext.Claims
            .OrderByDescending(c => c.CreatedAt)
            .Take(take)
            .Select(c => new ClaimListItem
            {
                ClaimId = c.ClaimId,
                Predicate = c.Predicate,
                Confidence = c.Confidence,
                Status = c.Status,
                ValidFrom = c.ValidFrom,
                ValidTo = c.ValidTo,
                EvidenceCount = c.Evidence.Count
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<ClaimCreatedResponse> CreateAsync(CreateClaimRequest request, CancellationToken cancellationToken)
    {
        if (request.Evidence.Count == 0)
        {
            throw new InvalidOperationException("Claim persistence requires at least one evidence row.");
        }

        var claim = new Claim
        {
            ClaimId = Guid.NewGuid(),
            SubjectEntityId = request.SubjectEntityId,
            Predicate = request.Predicate,
            ObjectEntityId = request.ObjectEntityId,
            LiteralValue = request.LiteralValue,
            ValueType = request.ValueType,
            Confidence = request.Confidence,
            Status = ClaimStatus.Active,
            Scope = request.Scope,
            ValidFrom = request.ValidFrom,
            ValidTo = request.ValidTo,
            Hash = request.Hash,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Evidence = request.Evidence.Select(e => new Evidence
            {
                EvidenceId = Guid.NewGuid(),
                SourceType = e.SourceType,
                SourceRef = e.SourceRef,
                ExcerptOrSummary = e.ExcerptOrSummary,
                Strength = e.Strength,
                CapturedAt = e.CapturedAt ?? DateTimeOffset.UtcNow,
                Metadata = e.Metadata
            }).ToList()
        };

        dbContext.Claims.Add(claim);

        var contradictoryClaims = await dbContext.Claims
            .Where(c =>
                c.SubjectEntityId == claim.SubjectEntityId &&
                c.Predicate == claim.Predicate &&
                c.Status == ClaimStatus.Active &&
                c.ClaimId != claim.ClaimId)
            .ToListAsync(cancellationToken);

        foreach (var existing in contradictoryClaims.Where(existing => IsDirectContradiction(existing, claim)))
        {
            dbContext.Contradictions.Add(new Contradiction
            {
                ContradictionId = Guid.NewGuid(),
                ClaimAId = existing.ClaimId,
                ClaimBId = claim.ClaimId,
                Type = "Direct",
                Severity = "High",
                Status = "Open",
                DetectedAt = DateTimeOffset.UtcNow
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new ClaimCreatedResponse
        {
            ClaimId = claim.ClaimId,
            SubjectEntityId = claim.SubjectEntityId,
            Predicate = claim.Predicate,
            LiteralValue = claim.LiteralValue,
            ObjectEntityId = claim.ObjectEntityId,
            ValueType = claim.ValueType,
            Confidence = claim.Confidence,
            Status = claim.Status,
            Scope = claim.Scope,
            CreatedAt = claim.CreatedAt
        };
    }

    public Task<Claim?> GetByHashAsync(string hash, CancellationToken cancellationToken)
    {
        return dbContext.Claims.FirstOrDefaultAsync(c => c.Hash == hash, cancellationToken);
    }

    public async Task<IReadOnlyList<QueryCandidate>> GetQueryCandidatesAsync(
        string? subjectFilter,
        CancellationToken cancellationToken,
        int maxCandidates = 0)
    {
        var claimQuery = dbContext.Claims
            .AsNoTracking()
            .Include(c => c.Evidence)
            .Where(c => c.Status != ClaimStatus.Retracted);

        if (!string.IsNullOrWhiteSpace(subjectFilter))
        {
            claimQuery = claimQuery.Where(c => c.Scope.Contains(subjectFilter));
        }

        if (maxCandidates > 0)
        {
            var bounded = Math.Clamp(maxCandidates, 1, 500);
            claimQuery = claimQuery
                .OrderByDescending(c => c.LastReinforcedAt ?? c.UpdatedAt)
                .ThenByDescending(c => c.CreatedAt)
                .Take(bounded);
        }

        var claims = await claimQuery.ToListAsync(cancellationToken);
        var claimIds = claims.Select(c => c.ClaimId).ToHashSet();

        var contradictions = await dbContext.Contradictions
            .AsNoTracking()
            .Where(c => claimIds.Contains(c.ClaimAId) || claimIds.Contains(c.ClaimBId))
            .ToListAsync(cancellationToken);

        var contradictionsByClaim = contradictions
            .SelectMany(c => new[]
            {
                new { ClaimId = c.ClaimAId, Contradiction = c },
                new { ClaimId = c.ClaimBId, Contradiction = c }
            })
            .Where(x => claimIds.Contains(x.ClaimId))
            .GroupBy(x => x.ClaimId, x => x.Contradiction)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<QueryContradictionItem>)g
                .Select(c => new QueryContradictionItem
                {
                    ContradictionId = c.ContradictionId,
                    Type = c.Type,
                    Severity = c.Severity,
                    Status = c.Status
                })
                .ToList());

        return claims
            .Select(c => new QueryCandidate
            {
                ClaimId = c.ClaimId,
                Predicate = c.Predicate,
                LiteralValue = c.LiteralValue,
                Confidence = c.Confidence,
                Scope = c.Scope,
                LastReinforcedAt = c.LastReinforcedAt,
                ValidFrom = c.ValidFrom,
                ValidTo = c.ValidTo,
                Evidence = c.Evidence.Select(e => new QueryEvidenceItem
                {
                    EvidenceId = e.EvidenceId,
                    SourceType = e.SourceType,
                    SourceRef = e.SourceRef,
                    Strength = e.Strength
                }).ToList(),
                Contradictions = contradictionsByClaim.GetValueOrDefault(c.ClaimId, [])
            })
            .ToList();
    }

    public async Task<QueryCandidate?> GetQueryCandidateByIdAsync(Guid claimId, CancellationToken cancellationToken)
    {
        var claim = await dbContext.Claims
            .AsNoTracking()
            .Include(c => c.Evidence)
            .FirstOrDefaultAsync(c => c.ClaimId == claimId, cancellationToken);
        if (claim is null)
        {
            return null;
        }

        var contradictions = await dbContext.Contradictions
            .Where(c => c.ClaimAId == claimId || c.ClaimBId == claimId)
            .Select(c => new QueryContradictionItem
            {
                ContradictionId = c.ContradictionId,
                Type = c.Type,
                Severity = c.Severity,
                Status = c.Status
            })
            .ToListAsync(cancellationToken);

        return new QueryCandidate
        {
            ClaimId = claim.ClaimId,
            Predicate = claim.Predicate,
            LiteralValue = claim.LiteralValue,
            Confidence = claim.Confidence,
            Scope = claim.Scope,
            LastReinforcedAt = claim.LastReinforcedAt,
            ValidFrom = claim.ValidFrom,
            ValidTo = claim.ValidTo,
            Evidence = claim.Evidence.Select(e => new QueryEvidenceItem
            {
                EvidenceId = e.EvidenceId,
                SourceType = e.SourceType,
                SourceRef = e.SourceRef,
                Strength = e.Strength
            }).ToList(),
            Contradictions = contradictions
        };
    }

    public async Task<ClaimLifecycleResponse> SupersedeAsync(Guid claimId, Guid replacementClaimId, CancellationToken cancellationToken)
    {
        var claim = await dbContext.Claims.FirstOrDefaultAsync(c => c.ClaimId == claimId, cancellationToken)
            ?? throw new KeyNotFoundException($"Claim {claimId} not found.");

        var replacement = await dbContext.Claims.FirstOrDefaultAsync(c => c.ClaimId == replacementClaimId, cancellationToken)
            ?? throw new KeyNotFoundException($"Replacement claim {replacementClaimId} not found.");

        claim.Status = ClaimStatus.Superseded;
        claim.UpdatedAt = DateTimeOffset.UtcNow;
        replacement.LastReinforcedAt = DateTimeOffset.UtcNow;
        replacement.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return new ClaimLifecycleResponse
        {
            ClaimId = claim.ClaimId,
            Status = claim.Status,
            UpdatedAt = claim.UpdatedAt
        };
    }

    public async Task<ClaimLifecycleResponse> RetractAsync(Guid claimId, CancellationToken cancellationToken)
    {
        var claim = await dbContext.Claims.FirstOrDefaultAsync(c => c.ClaimId == claimId, cancellationToken)
            ?? throw new KeyNotFoundException($"Claim {claimId} not found.");

        claim.Status = ClaimStatus.Retracted;
        claim.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return new ClaimLifecycleResponse
        {
            ClaimId = claim.ClaimId,
            Status = claim.Status,
            UpdatedAt = claim.UpdatedAt
        };
    }

    public async Task<Guid> CreateManualContradictionAsync(Guid claimId, string reason, CancellationToken cancellationToken)
    {
        _ = await dbContext.Claims.FirstOrDefaultAsync(c => c.ClaimId == claimId, cancellationToken)
            ?? throw new KeyNotFoundException($"Claim {claimId} not found.");

        var existing = await dbContext.Contradictions
            .FirstOrDefaultAsync(c => c.ClaimAId == claimId && c.ClaimBId == claimId, cancellationToken);
        if (existing is not null)
        {
            existing.Status = "Open";
            existing.ResolutionNotes = string.IsNullOrWhiteSpace(reason)
                ? existing.ResolutionNotes
                : reason;
            await dbContext.SaveChangesAsync(cancellationToken);
            return existing.ContradictionId;
        }

        var contradiction = new Contradiction
        {
            ContradictionId = Guid.NewGuid(),
            ClaimAId = claimId,
            ClaimBId = claimId,
            Type = "ManualFlag",
            Severity = "Medium",
            Status = "Open",
            DetectedAt = DateTimeOffset.UtcNow,
            ResolutionNotes = reason
        };

        dbContext.Contradictions.Add(contradiction);
        await dbContext.SaveChangesAsync(cancellationToken);
        return contradiction.ContradictionId;
    }

    public async Task<ClaimConfidenceUpdateResult?> TryApplyRecommendedConfidenceAsync(
        Guid claimId,
        double recommendedConfidence,
        double minDeltaToApply,
        double maxStep,
        CancellationToken cancellationToken)
    {
        var claim = await dbContext.Claims.FirstOrDefaultAsync(c => c.ClaimId == claimId, cancellationToken);
        if (claim is null)
        {
            return null;
        }

        if (claim.Status == ClaimStatus.Retracted)
        {
            return new ClaimConfidenceUpdateResult
            {
                ClaimId = claimId,
                PreviousConfidence = claim.Confidence,
                UpdatedConfidence = claim.Confidence,
                Applied = false,
                Reason = "claim_retracted"
            };
        }

        var clampedRecommended = Math.Clamp(recommendedConfidence, 0, 1);
        var delta = clampedRecommended - claim.Confidence;
        if (Math.Abs(delta) < Math.Max(0.0, minDeltaToApply))
        {
            return new ClaimConfidenceUpdateResult
            {
                ClaimId = claimId,
                PreviousConfidence = claim.Confidence,
                UpdatedConfidence = claim.Confidence,
                Applied = false,
                Reason = "delta_below_threshold"
            };
        }

        var limitedDelta = Math.Clamp(delta, -Math.Abs(maxStep), Math.Abs(maxStep));
        var updated = Math.Clamp(claim.Confidence + limitedDelta, 0, 1);
        if (Math.Abs(updated - claim.Confidence) < 0.0001)
        {
            return new ClaimConfidenceUpdateResult
            {
                ClaimId = claimId,
                PreviousConfidence = claim.Confidence,
                UpdatedConfidence = claim.Confidence,
                Applied = false,
                Reason = "no_effective_change"
            };
        }

        var previous = claim.Confidence;
        claim.Confidence = updated;
        claim.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new ClaimConfidenceUpdateResult
        {
            ClaimId = claimId,
            PreviousConfidence = previous,
            UpdatedConfidence = claim.Confidence,
            Applied = true,
            Reason = "applied"
        };
    }

    private static bool IsDirectContradiction(Claim existing, Claim incoming)
    {
        if (existing.ObjectEntityId.HasValue && incoming.ObjectEntityId.HasValue)
        {
            return existing.ObjectEntityId.Value != incoming.ObjectEntityId.Value;
        }

        var existingLiteral = existing.LiteralValue?.Trim();
        var incomingLiteral = incoming.LiteralValue?.Trim();

        return !string.IsNullOrWhiteSpace(existingLiteral)
               && !string.IsNullOrWhiteSpace(incomingLiteral)
               && !string.Equals(existingLiteral, incomingLiteral, StringComparison.OrdinalIgnoreCase);
    }
}
