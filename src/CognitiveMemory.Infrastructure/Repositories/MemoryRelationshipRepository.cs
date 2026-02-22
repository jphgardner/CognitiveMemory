using CognitiveMemory.Application.Abstractions;
using CognitiveMemory.Domain.Memory;
using CognitiveMemory.Infrastructure.Events;
using CognitiveMemory.Infrastructure.Companions;
using CognitiveMemory.Infrastructure.Persistence;
using CognitiveMemory.Infrastructure.Persistence.Entities;
using CognitiveMemory.Infrastructure.Relationships;
using Microsoft.EntityFrameworkCore;

namespace CognitiveMemory.Infrastructure.Repositories;

public sealed class MemoryRelationshipRepository(
    MemoryDbContext dbContext,
    ICompanionScopeResolver companionScopeResolver,
    IOutboxWriter outboxWriter,
    IRelationshipConfidencePolicy confidencePolicy) : IMemoryRelationshipRepository
{
    public async Task<MemoryRelationship> UpsertAsync(MemoryRelationship relationship, CancellationToken cancellationToken = default)
    {
        if (IsSelfLoop(relationship.FromType, relationship.FromId, relationship.ToType, relationship.ToId))
        {
            // Self-loop edges are degenerate and should not be persisted.
            return relationship;
        }

        var companionId = await companionScopeResolver.ResolveCompanionIdOrThrowAsync(relationship.SessionId, cancellationToken);
        var resolvedPolicy = confidencePolicy.Resolve(relationship.RelationshipType, relationship.Confidence, relationship.Strength);
        if (!resolvedPolicy.Accepted)
        {
            throw new InvalidOperationException(
                $"Relationship policy rejected '{resolvedPolicy.RelationshipType}' with confidence={resolvedPolicy.Confidence:0.###} strength={resolvedPolicy.Strength:0.###}. " +
                $"Minimum required confidence={resolvedPolicy.MinConfidence:0.###}, strength={resolvedPolicy.MinStrength:0.###}.");
        }

        var normalizedType = resolvedPolicy.RelationshipType;
        var existing = await dbContext.MemoryRelationships
            .FirstOrDefaultAsync(
                x => x.SessionId == relationship.SessionId
                     && x.CompanionId == companionId
                     && x.FromType == relationship.FromType.ToString()
                     && x.FromId == relationship.FromId
                     && x.ToType == relationship.ToType.ToString()
                     && x.ToId == relationship.ToId
                     && x.RelationshipType == normalizedType,
                cancellationToken);

        var now = DateTimeOffset.UtcNow;
        if (existing is null)
        {
            var entity = new MemoryRelationshipEntity
            {
                RelationshipId = relationship.RelationshipId,
                CompanionId = companionId,
                SessionId = relationship.SessionId,
                FromType = relationship.FromType.ToString(),
                FromId = relationship.FromId,
                ToType = relationship.ToType.ToString(),
                ToId = relationship.ToId,
                RelationshipType = normalizedType,
                Confidence = resolvedPolicy.Confidence,
                Strength = resolvedPolicy.Strength,
                Status = relationship.Status.ToString(),
                ValidFromUtc = relationship.ValidFromUtc,
                ValidToUtc = relationship.ValidToUtc,
                MetadataJson = relationship.MetadataJson,
                CreatedAtUtc = relationship.CreatedAtUtc,
                UpdatedAtUtc = now
            };
            dbContext.MemoryRelationships.Add(entity);
            outboxWriter.Enqueue(
                MemoryEventTypes.MemoryRelationshipCreated,
                aggregateType: "MemoryRelationship",
                aggregateId: entity.RelationshipId.ToString("N"),
                payload: new
                {
                    entity.RelationshipId,
                    entity.CompanionId,
                    entity.SessionId,
                    entity.FromType,
                    entity.FromId,
                    entity.ToType,
                    entity.ToId,
                    entity.RelationshipType,
                    entity.Confidence,
                    entity.Strength
                });
            await dbContext.SaveChangesAsync(cancellationToken);
            return ToDomain(entity);
        }

        existing.Confidence = Math.Max(existing.Confidence, resolvedPolicy.Confidence);
        existing.Strength = Math.Max(existing.Strength, resolvedPolicy.Strength);
        existing.Status = MemoryRelationshipStatus.Active.ToString();
        existing.ValidFromUtc = relationship.ValidFromUtc ?? existing.ValidFromUtc;
        existing.ValidToUtc = relationship.ValidToUtc;
        existing.MetadataJson = relationship.MetadataJson ?? existing.MetadataJson;
        existing.UpdatedAtUtc = now;
        outboxWriter.Enqueue(
            MemoryEventTypes.MemoryRelationshipUpdated,
            aggregateType: "MemoryRelationship",
            aggregateId: existing.RelationshipId.ToString("N"),
            payload: new
            {
                existing.RelationshipId,
                existing.CompanionId,
                existing.SessionId,
                existing.FromType,
                existing.FromId,
                existing.ToType,
                existing.ToId,
                existing.RelationshipType,
                existing.Confidence,
                existing.Strength,
                existing.Status
            });
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDomain(existing);
    }

    public async Task<bool> RetireAsync(Guid relationshipId, CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.MemoryRelationships.FirstOrDefaultAsync(x => x.RelationshipId == relationshipId, cancellationToken);
        if (existing is null)
        {
            return false;
        }

        existing.Status = MemoryRelationshipStatus.Retired.ToString();
        existing.ValidToUtc = DateTimeOffset.UtcNow;
        existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
        outboxWriter.Enqueue(
            MemoryEventTypes.MemoryRelationshipRetired,
            aggregateType: "MemoryRelationship",
            aggregateId: existing.RelationshipId.ToString("N"),
            payload: new
            {
                existing.RelationshipId,
                existing.CompanionId,
                existing.SessionId
            });
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<MemoryRelationship>> QueryBySessionAsync(
        string sessionId,
        string? relationshipType = null,
        MemoryRelationshipStatus? status = null,
        int take = 200,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.MemoryRelationships
            .AsNoTracking()
            .Where(x => x.SessionId == sessionId)
            .Where(x => !(x.FromType == x.ToType && x.FromId == x.ToId));

        var companionId = await companionScopeResolver.ResolveCompanionIdOrThrowAsync(sessionId, cancellationToken);
        query = query.Where(x => x.CompanionId == companionId);

        if (!string.IsNullOrWhiteSpace(relationshipType))
        {
            var normalized = relationshipType.Trim().ToLowerInvariant();
            query = query.Where(x => x.RelationshipType == normalized);
        }

        if (status.HasValue)
        {
            query = query.Where(x => x.Status == status.Value.ToString());
        }

        var rows = await query
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(Math.Clamp(take, 1, 1000))
            .ToListAsync(cancellationToken);

        return rows.Select(ToDomain).ToArray();
    }

    public async Task<IReadOnlyList<MemoryRelationship>> QueryByNodeAsync(
        string sessionId,
        MemoryNodeType nodeType,
        string nodeId,
        string? relationshipType = null,
        int take = 200,
        CancellationToken cancellationToken = default)
    {
        var companionId = await companionScopeResolver.ResolveCompanionIdOrThrowAsync(sessionId, cancellationToken);
        var typeText = nodeType.ToString();
        var query = dbContext.MemoryRelationships
            .AsNoTracking()
            .Where(
                x => x.SessionId == sessionId
                     && x.CompanionId == companionId
                     && x.Status == MemoryRelationshipStatus.Active.ToString()
                     && !(x.FromType == x.ToType && x.FromId == x.ToId)
                     && ((x.FromType == typeText && x.FromId == nodeId) || (x.ToType == typeText && x.ToId == nodeId)));

        if (!string.IsNullOrWhiteSpace(relationshipType))
        {
            var normalized = relationshipType.Trim().ToLowerInvariant();
            query = query.Where(x => x.RelationshipType == normalized);
        }

        var rows = await query
            .OrderByDescending(x => x.Strength)
            .ThenByDescending(x => x.UpdatedAtUtc)
            .Take(Math.Clamp(take, 1, 1000))
            .ToListAsync(cancellationToken);

        return rows.Select(ToDomain).ToArray();
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetSemanticRelationshipDegreeAsync(
        string sessionId,
        IReadOnlyList<Guid> semanticClaimIds,
        CancellationToken cancellationToken = default)
    {
        if (semanticClaimIds.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        var companionId = await companionScopeResolver.ResolveCompanionIdOrThrowAsync(sessionId, cancellationToken);
        var idTexts = semanticClaimIds.Select(x => x.ToString("N")).ToArray();
        var rows = await dbContext.MemoryRelationships
            .AsNoTracking()
            .Where(
                x => x.SessionId == sessionId
                     && x.CompanionId == companionId
                     && x.Status == MemoryRelationshipStatus.Active.ToString()
                     && !(x.FromType == x.ToType && x.FromId == x.ToId)
                     && ((x.FromType == MemoryNodeType.SemanticClaim.ToString() && idTexts.Contains(x.FromId))
                         || (x.ToType == MemoryNodeType.SemanticClaim.ToString() && idTexts.Contains(x.ToId))))
            .ToListAsync(cancellationToken);

        var output = new Dictionary<Guid, int>();
        foreach (var row in rows)
        {
            if (row.FromType == MemoryNodeType.SemanticClaim.ToString()
                && Guid.TryParseExact(row.FromId, "N", out var fromId))
            {
                output[fromId] = (output.TryGetValue(fromId, out var count) ? count : 0) + 1;
            }

            if (row.ToType == MemoryNodeType.SemanticClaim.ToString()
                && Guid.TryParseExact(row.ToId, "N", out var toId))
            {
                output[toId] = (output.TryGetValue(toId, out var count) ? count : 0) + 1;
            }
        }

        return output;
    }

    public async Task<MemoryRelationshipBackfillResult> BackfillAsync(string? sessionId = null, int take = 2000, CancellationToken cancellationToken = default)
    {
        var scanned = 0;
        var created = 0;
        var updated = 0;
        var companionSessionMap = await dbContext.Companions
            .AsNoTracking()
            .Where(x => !x.IsArchived)
            .ToDictionaryAsync(x => x.CompanionId, x => x.SessionId, cancellationToken);

        async Task TrackAsync(MemoryRelationship relationship)
        {
            var wasCreated = await UpsertTrackedAsync(relationship, cancellationToken);
            if (wasCreated)
            {
                created += 1;
            }
            else
            {
                updated += 1;
            }
        }

        IQueryable<SemanticClaimEntity> claimQuery = dbContext.SemanticClaims.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            var companionId = await companionScopeResolver.ResolveCompanionIdOrThrowAsync(sessionId, cancellationToken);
            var token = $"session:{sessionId}";
            claimQuery = claimQuery.Where(x => x.CompanionId == companionId && x.Subject.ToLower().Contains(token.ToLower()));
        }

        var claims = await claimQuery
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(Math.Clamp(take, 100, 20000))
            .ToListAsync(cancellationToken);
        scanned = claims.Count;

        var claimsById = claims.ToDictionary(x => x.ClaimId);

        foreach (var claim in claims)
        {
            var normalizedSubject = claim.Subject.Trim();
            if (normalizedSubject.StartsWith("session:", StringComparison.OrdinalIgnoreCase))
            {
                var sid = normalizedSubject["session:".Length..].Trim();
                if (!string.IsNullOrWhiteSpace(sid))
                {
                    await TrackAsync(
                        new MemoryRelationship(
                            Guid.NewGuid(),
                            sid,
                            MemoryNodeType.SemanticClaim,
                            claim.ClaimId.ToString("N"),
                            MemoryNodeType.SelfPreference,
                            claim.Predicate.Trim().ToLowerInvariant(),
                            "about",
                            0.7,
                            0.6,
                            MemoryRelationshipStatus.Active,
                            null,
                            null,
                            null,
                            DateTimeOffset.UtcNow,
                            DateTimeOffset.UtcNow));
                }
            }

            if (claim.SupersededByClaimId.HasValue)
            {
                var sid = ResolveSessionId(claim.CompanionId, claim.Subject, companionSessionMap);
                if (string.IsNullOrWhiteSpace(sid))
                {
                    continue;
                }

                await TrackAsync(
                    new MemoryRelationship(
                        Guid.NewGuid(),
                        sid!,
                        MemoryNodeType.SemanticClaim,
                        claim.ClaimId.ToString("N"),
                        MemoryNodeType.SemanticClaim,
                        claim.SupersededByClaimId.Value.ToString("N"),
                        "superseded_by",
                        0.95,
                        0.95,
                        MemoryRelationshipStatus.Active,
                        null,
                        null,
                        null,
                        DateTimeOffset.UtcNow,
                        DateTimeOffset.UtcNow));
            }
        }

        var contradictions = await dbContext.ClaimContradictions
            .AsNoTracking()
            .OrderByDescending(x => x.DetectedAtUtc)
            .Take(Math.Clamp(take, 100, 20000))
            .ToListAsync(cancellationToken);
        foreach (var row in contradictions)
        {
            var claimA = claimsById.GetValueOrDefault(row.ClaimAId)
                         ?? await dbContext.SemanticClaims.AsNoTracking().FirstOrDefaultAsync(x => x.ClaimId == row.ClaimAId, cancellationToken);
            var sid = claimA is null ? null : ResolveSessionId(claimA.CompanionId, claimA.Subject, companionSessionMap);
            if (string.IsNullOrWhiteSpace(sid))
            {
                continue;
            }

            await TrackAsync(
                new MemoryRelationship(
                    Guid.NewGuid(),
                    sid!,
                    MemoryNodeType.SemanticClaim,
                    row.ClaimAId.ToString("N"),
                    MemoryNodeType.SemanticClaim,
                    row.ClaimBId.ToString("N"),
                    "contradicts",
                    0.9,
                    0.9,
                    MemoryRelationshipStatus.Active,
                    null,
                    null,
                    $"{{\"severity\":\"{row.Severity}\",\"type\":\"{row.Type}\"}}",
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow));
        }

        // Evidence-based cross-layer edges: semantic -> source node.
        var claimIds = claims.Select(x => x.ClaimId).ToArray();
        var evidences = await dbContext.ClaimEvidence
            .AsNoTracking()
            .Where(x => claimIds.Contains(x.ClaimId))
            .OrderByDescending(x => x.CapturedAtUtc)
            .Take(Math.Clamp(take * 3, 200, 60000))
            .ToListAsync(cancellationToken);
        foreach (var evidence in evidences)
        {
            if (!claimsById.TryGetValue(evidence.ClaimId, out var parentClaim))
            {
                continue;
            }

            var sid = ResolveSessionId(parentClaim.CompanionId, parentClaim.Subject, companionSessionMap);
            if (string.IsNullOrWhiteSpace(sid))
            {
                continue;
            }

            var normalizedSource = evidence.SourceType.Trim().ToLowerInvariant();
            if (normalizedSource.Contains("episodic", StringComparison.Ordinal)
                && TryExtractGuid(evidence.SourceReference, out var eventId))
            {
                await TrackAsync(
                    new MemoryRelationship(
                        Guid.NewGuid(),
                        sid!,
                        MemoryNodeType.SemanticClaim,
                        evidence.ClaimId.ToString("N"),
                        MemoryNodeType.EpisodicEvent,
                        eventId.ToString("N"),
                        "supported_by",
                        Math.Clamp(evidence.Strength, 0, 1),
                        Math.Clamp(evidence.Strength, 0, 1),
                        MemoryRelationshipStatus.Active,
                        null,
                        null,
                        null,
                        DateTimeOffset.UtcNow,
                        DateTimeOffset.UtcNow));
                continue;
            }

            if (normalizedSource.Contains("procedural", StringComparison.Ordinal)
                && TryExtractGuid(evidence.SourceReference, out var routineId))
            {
                await TrackAsync(
                    new MemoryRelationship(
                        Guid.NewGuid(),
                        sid!,
                        MemoryNodeType.SemanticClaim,
                        evidence.ClaimId.ToString("N"),
                        MemoryNodeType.ProceduralRoutine,
                        routineId.ToString("N"),
                        "supported_by_routine",
                        Math.Clamp(evidence.Strength, 0, 1),
                        Math.Clamp(evidence.Strength, 0, 1),
                        MemoryRelationshipStatus.Active,
                        null,
                        null,
                        null,
                        DateTimeOffset.UtcNow,
                        DateTimeOffset.UtcNow));
                continue;
            }

            if (normalizedSource.Contains("self", StringComparison.Ordinal))
            {
                await TrackAsync(
                    new MemoryRelationship(
                        Guid.NewGuid(),
                        sid!,
                        MemoryNodeType.SemanticClaim,
                        evidence.ClaimId.ToString("N"),
                        MemoryNodeType.SelfPreference,
                        evidence.SourceReference.Trim().ToLowerInvariant(),
                        "supported_by_self",
                        Math.Clamp(evidence.Strength, 0, 1),
                        Math.Clamp(evidence.Strength, 0, 1),
                        MemoryRelationshipStatus.Active,
                        null,
                        null,
                        null,
                        DateTimeOffset.UtcNow,
                        DateTimeOffset.UtcNow));
            }
        }

        // Procedural-to-semantic dependency edges.
        var routines = await dbContext.ProceduralRoutines
            .AsNoTracking()
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(Math.Clamp(take, 100, 10000))
            .ToListAsync(cancellationToken);
        foreach (var routine in routines)
        {
            var routineText = $"{routine.Trigger} {routine.Name} {routine.Outcome}".ToLowerInvariant();
            var candidates = claims
                .Where(c => c.CompanionId == routine.CompanionId)
                .Where(
                    c => routineText.Contains(c.Predicate.ToLowerInvariant(), StringComparison.Ordinal)
                         || c.Value.ToLowerInvariant().Contains(routine.Trigger.ToLowerInvariant(), StringComparison.Ordinal)
                         || c.Predicate.ToLowerInvariant().Contains(routine.Trigger.ToLowerInvariant(), StringComparison.Ordinal))
                .OrderByDescending(c => c.Confidence)
                .Take(6)
                .ToArray();
            foreach (var claim in candidates)
            {
                var sid = ResolveSessionId(claim.CompanionId, claim.Subject, companionSessionMap);
                if (string.IsNullOrWhiteSpace(sid))
                {
                    continue;
                }

                await TrackAsync(
                    new MemoryRelationship(
                        Guid.NewGuid(),
                        sid!,
                        MemoryNodeType.ProceduralRoutine,
                        routine.RoutineId.ToString("N"),
                        MemoryNodeType.SemanticClaim,
                        claim.ClaimId.ToString("N"),
                        "depends_on",
                        0.68,
                        0.62,
                        MemoryRelationshipStatus.Active,
                        null,
                        null,
                        null,
                        DateTimeOffset.UtcNow,
                        DateTimeOffset.UtcNow));
            }
        }

        // Temporal chain edges across episodic events in each session.
        var events = await dbContext.EpisodicMemoryEvents
            .AsNoTracking()
            .OrderBy(x => x.CompanionId)
            .ThenBy(x => x.SessionId)
            .ThenBy(x => x.OccurredAt)
            .Take(Math.Clamp(take * 4, 500, 120000))
            .ToListAsync(cancellationToken);
        foreach (var group in events.GroupBy(x => new { x.CompanionId, x.SessionId }))
        {
            EpisodicMemoryEventEntity? previous = null;
            foreach (var current in group)
            {
                if (previous is not null)
                {
                    await TrackAsync(
                        new MemoryRelationship(
                            Guid.NewGuid(),
                            group.Key.SessionId,
                            MemoryNodeType.EpisodicEvent,
                            previous.EventId.ToString("N"),
                            MemoryNodeType.EpisodicEvent,
                            current.EventId.ToString("N"),
                            "follows_after",
                            1.0,
                            0.55,
                            MemoryRelationshipStatus.Active,
                            previous.OccurredAt,
                            current.OccurredAt,
                            null,
                            DateTimeOffset.UtcNow,
                            DateTimeOffset.UtcNow));
                }

                previous = current;
            }
        }

        return new MemoryRelationshipBackfillResult(scanned, created, updated);
    }

    private static string? TryParseSessionId(string subject)
    {
        var normalized = subject.Trim();
        if (!normalized.StartsWith("session:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var sid = normalized["session:".Length..].Trim();
        return sid.Length == 0 ? null : sid;
    }

    private static string? ResolveSessionId(
        Guid companionId,
        string subject,
        IReadOnlyDictionary<Guid, string> companionSessionMap)
    {
        var parsed = TryParseSessionId(subject);
        if (!string.IsNullOrWhiteSpace(parsed))
        {
            return parsed;
        }

        return companionSessionMap.TryGetValue(companionId, out var fallbackSessionId) && !string.IsNullOrWhiteSpace(fallbackSessionId)
            ? fallbackSessionId
            : null;
    }

    private async Task<bool> UpsertTrackedAsync(
        MemoryRelationship relationship,
        CancellationToken cancellationToken)
    {
        if (IsSelfLoop(relationship.FromType, relationship.FromId, relationship.ToType, relationship.ToId))
        {
            return false;
        }

        var normalizedType = relationship.RelationshipType.Trim().ToLowerInvariant();
        var exists = await dbContext.MemoryRelationships
            .AsNoTracking()
            .AnyAsync(
                x => x.SessionId == relationship.SessionId
                     && x.FromType == relationship.FromType.ToString()
                     && x.FromId == relationship.FromId
                     && x.ToType == relationship.ToType.ToString()
                     && x.ToId == relationship.ToId
                     && x.RelationshipType == normalizedType,
                cancellationToken);

        _ = await UpsertAsync(relationship, cancellationToken);
        return !exists;
    }

    private static bool IsSelfLoop(MemoryNodeType fromType, string fromId, MemoryNodeType toType, string toId)
    {
        if (fromType != toType)
        {
            return false;
        }

        return NormalizeNodeId(fromType, fromId) == NormalizeNodeId(toType, toId);
    }

    private static string NormalizeNodeId(MemoryNodeType nodeType, string nodeId)
    {
        var trimmed = (nodeId ?? string.Empty).Trim();
        if (nodeType == MemoryNodeType.SelfPreference)
        {
            return trimmed.ToLowerInvariant();
        }

        return Guid.TryParse(trimmed, out var parsed) ? parsed.ToString("N") : trimmed.ToLowerInvariant();
    }

    private static bool TryExtractGuid(string input, out Guid guid)
    {
        guid = Guid.Empty;
        var trimmed = input.Trim();
        if (Guid.TryParse(trimmed, out guid))
        {
            return true;
        }

        var parts = trimmed.Split([':', '/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts.Reverse())
        {
            if (Guid.TryParse(part, out guid))
            {
                return true;
            }
        }

        return false;
    }

    private static MemoryRelationship ToDomain(MemoryRelationshipEntity x)
        => new(
            x.RelationshipId,
            x.SessionId,
            Enum.TryParse<MemoryNodeType>(x.FromType, true, out var fromType) ? fromType : MemoryNodeType.SemanticClaim,
            x.FromId,
            Enum.TryParse<MemoryNodeType>(x.ToType, true, out var toType) ? toType : MemoryNodeType.SemanticClaim,
            x.ToId,
            x.RelationshipType,
            x.Confidence,
            x.Strength,
            Enum.TryParse<MemoryRelationshipStatus>(x.Status, true, out var status) ? status : MemoryRelationshipStatus.Active,
            x.ValidFromUtc,
            x.ValidToUtc,
            x.MetadataJson,
            x.CreatedAtUtc,
            x.UpdatedAtUtc);
}
