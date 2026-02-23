using CognitiveMemory.Application.Abstractions;
using CognitiveMemory.Domain.Memory;
using CognitiveMemory.Infrastructure.Events;
using CognitiveMemory.Infrastructure.Persistence;
using CognitiveMemory.Infrastructure.Persistence.Entities;
using CognitiveMemory.Infrastructure.Companions;
using CognitiveMemory.Infrastructure.SemanticKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CognitiveMemory.Infrastructure.Repositories;

public sealed class SemanticMemoryRepository(
    MemoryDbContext dbContext,
    IOutboxWriter outboxWriter,
    ICompanionScopeResolver companionScopeResolver,
    ITextEmbeddingGenerator embeddingGenerator,
    SemanticKernelOptions options,
    ILogger<SemanticMemoryRepository> logger) : ISemanticMemoryRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<SemanticClaim> CreateClaimAsync(SemanticClaim claim, CancellationToken cancellationToken = default)
        => await CreateClaimAsync(await ResolveCompanionIdFromSubjectAsync(claim.Subject, cancellationToken), claim, cancellationToken);

    public async Task<SemanticClaim> CreateClaimAsync(Guid companionId, SemanticClaim claim, CancellationToken cancellationToken = default)
    {
        var entity = new SemanticClaimEntity
        {
            ClaimId = claim.ClaimId,
            CompanionId = companionId,
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
        outboxWriter.Enqueue(
            MemoryEventTypes.SemanticClaimCreated,
            aggregateType: "SemanticClaim",
            aggregateId: claim.ClaimId.ToString("N"),
            payload: new
            {
                companionId,
                claim.ClaimId,
                claim.Subject,
                claim.Predicate,
                claim.Value,
                claim.Confidence,
                claim.Scope,
                claim.Status
            });
        await dbContext.SaveChangesAsync(cancellationToken);
        _ = await UpsertClaimEmbeddingAsync(entity, cancellationToken);
        return claim;
    }

    public async Task<SemanticClaim?> GetByIdAsync(Guid claimId, CancellationToken cancellationToken = default)
    {
        var row = await dbContext.SemanticClaims.AsNoTracking().FirstOrDefaultAsync(x => x.ClaimId == claimId, cancellationToken);
        return row is null ? null : ToDomain(row);
    }

    public async Task<SemanticClaim?> GetByIdAsync(Guid companionId, Guid claimId, CancellationToken cancellationToken = default)
    {
        var row = await dbContext.SemanticClaims
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ClaimId == claimId && x.CompanionId == companionId, cancellationToken);
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
        outboxWriter.Enqueue(
            MemoryEventTypes.SemanticClaimSuperseded,
            aggregateType: "SemanticClaim",
            aggregateId: claimId.ToString("N"),
            payload: new
            {
                claimId,
                supersededByClaimId
            });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task SupersedeAsync(Guid companionId, Guid claimId, Guid supersededByClaimId, CancellationToken cancellationToken = default)
    {
        var row = await dbContext.SemanticClaims.FirstOrDefaultAsync(x => x.ClaimId == claimId && x.CompanionId == companionId, cancellationToken)
                  ?? throw new InvalidOperationException("Claim not found.");
        row.Status = SemanticClaimStatus.Superseded.ToString();
        row.SupersededByClaimId = supersededByClaimId;
        row.ValidToUtc = DateTimeOffset.UtcNow;
        row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        outboxWriter.Enqueue(
            MemoryEventTypes.SemanticClaimSuperseded,
            aggregateType: "SemanticClaim",
            aggregateId: claimId.ToString("N"),
            payload: new { companionId, claimId, supersededByClaimId });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<ClaimEvidence> AddEvidenceAsync(ClaimEvidence evidence, CancellationToken cancellationToken = default)
        => await AddEvidenceAsync(await ResolveCompanionIdForClaimAsync(evidence.ClaimId, cancellationToken), evidence, cancellationToken);

    public async Task<ClaimEvidence> AddEvidenceAsync(Guid companionId, ClaimEvidence evidence, CancellationToken cancellationToken = default)
    {
        var entity = new ClaimEvidenceEntity
        {
            EvidenceId = evidence.EvidenceId,
            CompanionId = companionId,
            ClaimId = evidence.ClaimId,
            SourceType = evidence.SourceType,
            SourceReference = evidence.SourceReference,
            ExcerptOrSummary = evidence.ExcerptOrSummary,
            Strength = evidence.Strength,
            CapturedAtUtc = evidence.CapturedAtUtc
        };

        dbContext.ClaimEvidence.Add(entity);
        outboxWriter.Enqueue(
            MemoryEventTypes.SemanticEvidenceAdded,
            aggregateType: "SemanticClaim",
            aggregateId: evidence.ClaimId.ToString("N"),
            payload: new
            {
                companionId,
                evidence.EvidenceId,
                evidence.ClaimId,
                evidence.SourceType,
                evidence.SourceReference,
                evidence.Strength
            });
        await dbContext.SaveChangesAsync(cancellationToken);
        return evidence;
    }

    public async Task<ClaimContradiction> AddContradictionAsync(ClaimContradiction contradiction, CancellationToken cancellationToken = default)
        => await AddContradictionAsync(await ResolveCompanionIdForClaimAsync(contradiction.ClaimAId, cancellationToken), contradiction, cancellationToken);

    public async Task<ClaimContradiction> AddContradictionAsync(Guid companionId, ClaimContradiction contradiction, CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.ClaimContradictions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.CompanionId == companionId
                     && ((x.ClaimAId == contradiction.ClaimAId && x.ClaimBId == contradiction.ClaimBId)
                     || (x.ClaimAId == contradiction.ClaimBId && x.ClaimBId == contradiction.ClaimAId)),
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
            CompanionId = companionId,
            ClaimAId = contradiction.ClaimAId,
            ClaimBId = contradiction.ClaimBId,
            Type = contradiction.Type,
            Severity = contradiction.Severity,
            DetectedAtUtc = contradiction.DetectedAtUtc,
            Status = contradiction.Status
        };

        dbContext.ClaimContradictions.Add(entity);
        outboxWriter.Enqueue(
            MemoryEventTypes.SemanticContradictionAdded,
            aggregateType: "SemanticClaim",
            aggregateId: contradiction.ClaimAId.ToString("N"),
            payload: new
            {
                companionId,
                contradiction.ContradictionId,
                contradiction.ClaimAId,
                contradiction.ClaimBId,
                contradiction.Type,
                contradiction.Severity,
                contradiction.Status
            });
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
        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new InvalidOperationException("Companion-scoped semantic search is required.");
        }

        var companionId = await ResolveCompanionIdFromSubjectAsync(subject!, cancellationToken);
        return await QueryClaimsAsync(companionId, subject, predicate, status, take, cancellationToken);
    }

    public async Task<IReadOnlyList<SemanticClaim>> QueryClaimsAsync(
        Guid companionId,
        string? subject = null,
        string? predicate = null,
        SemanticClaimStatus? status = null,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.SemanticClaims
            .AsNoTracking()
            .Where(x => x.CompanionId == companionId)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(subject))
        {
            var normalizedSubject = subject.Trim();
            var pattern = SqlLikePattern.Contains(normalizedSubject);
            query = query.Where(x => EF.Functions.ILike(x.Subject, pattern));
        }

        if (!string.IsNullOrWhiteSpace(predicate))
        {
            var normalizedPredicate = predicate.Trim();
            var pattern = SqlLikePattern.Contains(normalizedPredicate);
            query = query.Where(x => EF.Functions.ILike(x.Predicate, pattern));
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
        throw new InvalidOperationException("Companion-scoped semantic search is required.");
    }

    public async Task<IReadOnlyList<SemanticClaim>> SearchClaimsAsync(
        Guid companionId,
        string query,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        var cappedTake = Math.Clamp(take, 1, 500);
        if (string.IsNullOrWhiteSpace(query))
        {
            return await QueryClaimsAsync(companionId, take: cappedTake, cancellationToken: cancellationToken);
        }

        var normalizedTerms = query
            .Trim()
            .ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length >= 2)
            .Distinct(StringComparer.Ordinal)
            .Take(10)
            .ToArray();

        var lexicalTake = Math.Clamp(cappedTake * Math.Max(1, options.LexicalCandidateMultiplier), 1, 2000);
        var lexicalRows = await BuildLexicalQuery(companionId, normalizedTerms)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(lexicalTake)
            .ToListAsync(cancellationToken);

        var lexicalScores = ScoreLexical(normalizedTerms, lexicalRows);
        if (options.EnableVectorSearch && !string.IsNullOrWhiteSpace(options.EmbeddingModelId))
        {
            await BackfillMissingEmbeddingsAsync(
                lexicalRows.Select(x => x.ClaimId).Take(Math.Max(1, options.LazyEmbeddingBackfillTake)).ToArray(),
                cancellationToken);
        }

        var vectorScores = await ScoreVectorAsync(companionId, query, cappedTake, cancellationToken);
        var fused = FuseAndSort(lexicalScores, vectorScores, options.HybridRrfK, cappedTake)
            .Select(x => x.claim)
            .ToArray();

        if (fused.Length > 0)
        {
            logger.LogInformation(
                "Semantic hybrid retrieval completed. Query={Query} Lexical={LexicalCount} Vector={VectorCount} Returned={Returned}",
                Truncate(query, 120),
                lexicalScores.Count,
                vectorScores.Count,
                fused.Length);
            return fused;
        }

        var fallbackRows = await dbContext.SemanticClaims
            .AsNoTracking()
            .Where(x => x.CompanionId == companionId)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(cappedTake)
            .ToListAsync(cancellationToken);
        return fallbackRows.Select(ToDomain).ToArray();
    }

    public async Task<int> DecayActiveClaimsAsync(DateTimeOffset staleBeforeUtc, double decayStep, double minConfidence, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Companion-scoped semantic decay is required.");
    }

    public async Task<int> DecayActiveClaimsAsync(
        Guid companionId,
        DateTimeOffset staleBeforeUtc,
        double decayStep,
        double minConfidence,
        CancellationToken cancellationToken = default)
    {
        var claims = await dbContext.SemanticClaims
            .Where(x => x.CompanionId == companionId && x.Status == SemanticClaimStatus.Active.ToString() && x.UpdatedAtUtc <= staleBeforeUtc)
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

    private IQueryable<SemanticClaimEntity> BuildLexicalQuery(Guid companionId, string[] normalizedTerms)
    {
        IQueryable<SemanticClaimEntity> queryable = dbContext.SemanticClaims
            .AsNoTracking()
            .Where(x => x.CompanionId == companionId);
        if (normalizedTerms.Length == 0)
        {
            return queryable;
        }

        // OR query for recall; ranking handles precision.
        var first = normalizedTerms[0];
        var firstPattern = SqlLikePattern.Contains(first);
        queryable = queryable.Where(
            x => EF.Functions.ILike(x.Subject, firstPattern)
                 || EF.Functions.ILike(x.Predicate, firstPattern)
                 || EF.Functions.ILike(x.Value, firstPattern)
                 || EF.Functions.ILike(x.Scope, firstPattern));

        for (var i = 1; i < normalizedTerms.Length; i++)
        {
            var term = normalizedTerms[i];
            var termPattern = SqlLikePattern.Contains(term);
            queryable = queryable.Union(
                dbContext.SemanticClaims.AsNoTracking().Where(
                    x => x.CompanionId == companionId
                         && (EF.Functions.ILike(x.Subject, termPattern)
                         || EF.Functions.ILike(x.Predicate, termPattern)
                         || EF.Functions.ILike(x.Value, termPattern)
                         || EF.Functions.ILike(x.Scope, termPattern))));
        }

        return queryable;
    }

    private Dictionary<Guid, (SemanticClaim claim, double score)> ScoreLexical(string[] normalizedTerms, IReadOnlyList<SemanticClaimEntity> rows)
    {
        var output = new Dictionary<Guid, (SemanticClaim claim, double score)>(rows.Count);
        foreach (var row in rows)
        {
            var blob = $"{row.Subject} {row.Predicate} {row.Value} {row.Scope}".ToLowerInvariant();
            var matchCount = normalizedTerms.Count(term => blob.Contains(term, StringComparison.Ordinal));
            var recencyBoost = 1d / (1d + Math.Max(0d, (DateTimeOffset.UtcNow - row.UpdatedAtUtc).TotalDays));
            var score = matchCount + (0.2d * recencyBoost);
            output[row.ClaimId] = (ToDomain(row), score);
        }

        return output;
    }

    private async Task<Dictionary<Guid, (SemanticClaim claim, double score)>> ScoreVectorAsync(
        Guid companionId,
        string query,
        int take,
        CancellationToken cancellationToken)
    {
        var output = new Dictionary<Guid, (SemanticClaim claim, double score)>();
        if (!options.EnableVectorSearch || string.IsNullOrWhiteSpace(options.EmbeddingModelId))
        {
            return output;
        }

        var queryEmbedding = await embeddingGenerator.GenerateAsync(query, cancellationToken);
        if (queryEmbedding is null || queryEmbedding.Length == 0)
        {
            return output;
        }

        var vectorTake = Math.Clamp(take * Math.Max(1, options.VectorCandidateMultiplier), 1, 2000);
        var pgVectorScores = await TryScoreVectorUsingPgvectorAsync(companionId, queryEmbedding, vectorTake, cancellationToken);
        if (pgVectorScores.Count > 0)
        {
            return pgVectorScores;
        }

        var embeddings = await dbContext.SemanticClaimEmbeddings
            .AsNoTracking()
            .OrderByDescending(x => x.EmbeddedAtUtc)
            .Take(Math.Clamp(options.MaxVectorCandidatePool, 100, 20000))
            .ToListAsync(cancellationToken);
        if (embeddings.Count == 0)
        {
            return output;
        }

        var embeddingById = new Dictionary<Guid, (float[] vector, DateTimeOffset embeddedAtUtc)>(embeddings.Count);
        foreach (var row in embeddings)
        {
            var vector = ParseVector(row.VectorJson);
            if (vector is null || vector.Length == 0 || vector.Length != queryEmbedding.Length)
            {
                continue;
            }

            embeddingById[row.ClaimId] = (vector, row.EmbeddedAtUtc);
        }

        if (embeddingById.Count == 0)
        {
            return output;
        }

        var claimIds = embeddingById.Keys.ToArray();
        var claims = await dbContext.SemanticClaims
            .AsNoTracking()
            .Where(x => x.CompanionId == companionId && claimIds.Contains(x.ClaimId))
            .ToListAsync(cancellationToken);

        var scored = new List<(SemanticClaimEntity claim, double score)>(claims.Count);
        foreach (var claim in claims)
        {
            if (!embeddingById.TryGetValue(claim.ClaimId, out var vectorData))
            {
                continue;
            }

            var similarity = CosineSimilarity(queryEmbedding, vectorData.vector);
            scored.Add((claim, similarity));
        }

        foreach (var item in scored.OrderByDescending(x => x.score).Take(vectorTake))
        {
            output[item.claim.ClaimId] = (ToDomain(item.claim), item.score);
        }

        return output;
    }

    private async Task<Dictionary<Guid, (SemanticClaim claim, double score)>> TryScoreVectorUsingPgvectorAsync(
        Guid companionId,
        float[] queryEmbedding,
        int vectorTake,
        CancellationToken cancellationToken)
    {
        var output = new Dictionary<Guid, (SemanticClaim claim, double score)>();

        var queryLiteral = ToPgvectorLiteral(queryEmbedding);
        var modelId = options.EmbeddingModelId!;
        var dimensions = queryEmbedding.Length;
        var distanceRows = new List<(Guid claimId, double distance)>(vectorTake);

        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT "ClaimId", ("Embedding" <=> CAST(@query AS vector)) AS distance
                FROM "SemanticClaimEmbeddings"
                WHERE "Embedding" IS NOT NULL
                  AND "ModelId" = @model
                  AND "Dimensions" = @dimensions
                ORDER BY "Embedding" <=> CAST(@query AS vector)
                LIMIT @take;
                """;

            command.Parameters.Add(new NpgsqlParameter("@query", queryLiteral));
            command.Parameters.Add(new NpgsqlParameter("@model", modelId));
            command.Parameters.Add(new NpgsqlParameter("@dimensions", dimensions));
            command.Parameters.Add(new NpgsqlParameter("@take", vectorTake));

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (reader.IsDBNull(0) || reader.IsDBNull(1))
                {
                    continue;
                }

                distanceRows.Add((reader.GetGuid(0), reader.GetDouble(1)));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "pgvector retrieval unavailable; falling back to in-process vector scoring.");
            return output;
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }

        if (distanceRows.Count == 0)
        {
            return output;
        }

        var claimIds = distanceRows.Select(x => x.claimId).ToArray();
        var claims = await dbContext.SemanticClaims
            .AsNoTracking()
            .Where(x => x.CompanionId == companionId && claimIds.Contains(x.ClaimId))
            .ToListAsync(cancellationToken);
        var claimsById = claims.ToDictionary(x => x.ClaimId, x => x);

        foreach (var row in distanceRows)
        {
            if (!claimsById.TryGetValue(row.claimId, out var claim))
            {
                continue;
            }

            output[row.claimId] = (ToDomain(claim), 1d - row.distance);
        }

        return output;
    }

    private static IReadOnlyList<(SemanticClaim claim, double fusedScore)> FuseAndSort(
        Dictionary<Guid, (SemanticClaim claim, double score)> lexicalScores,
        Dictionary<Guid, (SemanticClaim claim, double score)> vectorScores,
        int rrfK,
        int take)
    {
        var k = Math.Clamp(rrfK, 1, 2000);
        var fused = new Dictionary<Guid, (SemanticClaim claim, double score)>();

        var lexicalRanking = lexicalScores
            .OrderByDescending(x => x.Value.score)
            .Select((x, index) => (id: x.Key, claim: x.Value.claim, rank: index + 1));
        foreach (var item in lexicalRanking)
        {
            fused[item.id] = (item.claim, 1d / (k + item.rank));
        }

        var vectorRanking = vectorScores
            .OrderByDescending(x => x.Value.score)
            .Select((x, index) => (id: x.Key, claim: x.Value.claim, rank: index + 1));
        foreach (var item in vectorRanking)
        {
            var score = 1d / (k + item.rank);
            if (fused.TryGetValue(item.id, out var existing))
            {
                fused[item.id] = (existing.claim, existing.score + score);
                continue;
            }

            fused[item.id] = (item.claim, score);
        }

        return fused
            .OrderByDescending(x => x.Value.score)
            .ThenByDescending(x => x.Value.claim.UpdatedAtUtc)
            .Take(Math.Clamp(take, 1, 500))
            .Select(x => (x.Value.claim, x.Value.score))
            .ToArray();
    }

    private async Task BackfillMissingEmbeddingsAsync(Guid[] candidateClaimIds, CancellationToken cancellationToken)
    {
        if (!options.EnableVectorSearch || string.IsNullOrWhiteSpace(options.EmbeddingModelId) || candidateClaimIds.Length == 0)
        {
            return;
        }

        var existingRows = await dbContext.SemanticClaimEmbeddings
            .AsNoTracking()
            .Where(x => candidateClaimIds.Contains(x.ClaimId))
            .ToListAsync(cancellationToken);
        var currentModel = options.EmbeddingModelId ?? string.Empty;
        var existingById = existingRows.ToDictionary(x => x.ClaimId, x => x, EqualityComparer<Guid>.Default);
        var staleOrMissingIds = candidateClaimIds
            .Where(id => !existingById.TryGetValue(id, out var existing)
                         || !string.Equals(existing.ModelId, currentModel, StringComparison.Ordinal))
            .ToArray();
        if (staleOrMissingIds.Length == 0)
        {
            return;
        }

        var claims = await dbContext.SemanticClaims
            .Where(x => staleOrMissingIds.Contains(x.ClaimId))
            .ToListAsync(cancellationToken);

        var updates = 0;
        foreach (var claim in claims)
        {
            updates += await UpsertClaimEmbeddingAsync(claim, cancellationToken) ? 1 : 0;
        }

        if (updates > 0)
        {
            logger.LogInformation("Semantic embedding lazy backfill completed. Added={Added}", updates);
        }
    }

    private async Task<Guid> ResolveCompanionIdFromSubjectAsync(string subject, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new InvalidOperationException("Semantic subject is required to resolve companion scope.");
        }

        var normalized = subject.Trim();
        const string sessionPrefix = "session:";
        if (normalized.StartsWith(sessionPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var sessionId = normalized[sessionPrefix.Length..].Trim();
            return await companionScopeResolver.ResolveCompanionIdOrThrowAsync(sessionId, cancellationToken);
        }

        const string companionPrefix = "companion:";
        if (normalized.StartsWith(companionPrefix, StringComparison.OrdinalIgnoreCase)
            && Guid.TryParse(normalized[companionPrefix.Length..].Trim(), out var companionId))
        {
            return companionId;
        }

        throw new InvalidOperationException($"Semantic subject '{subject}' is outside companion scope.");
    }

    private async Task<Guid> ResolveCompanionIdForClaimAsync(Guid claimId, CancellationToken cancellationToken)
    {
        var companionId = await dbContext.SemanticClaims
            .AsNoTracking()
            .Where(x => x.ClaimId == claimId)
            .Select(x => (Guid?)x.CompanionId)
            .FirstOrDefaultAsync(cancellationToken);

        if (!companionId.HasValue)
        {
            throw new InvalidOperationException($"Cannot resolve companion scope for claim '{claimId:D}'.");
        }

        return companionId.Value;
    }

    private async Task<bool> UpsertClaimEmbeddingAsync(SemanticClaimEntity claim, CancellationToken cancellationToken)
    {
        if (!options.EnableVectorSearch || string.IsNullOrWhiteSpace(options.EmbeddingModelId))
        {
            return false;
        }

        var searchText = BuildSearchText(claim);
        var hash = ComputeContentHash(searchText);

        var existing = await dbContext.SemanticClaimEmbeddings
            .FirstOrDefaultAsync(x => x.ClaimId == claim.ClaimId, cancellationToken);
        if (existing is not null && string.Equals(existing.ContentHash, hash, StringComparison.Ordinal))
        {
            return false;
        }

        var embedding = await embeddingGenerator.GenerateAsync(searchText, cancellationToken);
        if (embedding is null || embedding.Length == 0)
        {
            return false;
        }

        var vectorJson = JsonSerializer.Serialize(embedding, JsonOptions);
        var now = DateTimeOffset.UtcNow;
        var vectorLiteral = ToPgvectorLiteral(embedding);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "SemanticClaimEmbeddings" ("ClaimId", "ModelId", "Dimensions", "VectorJson", "ContentHash", "EmbeddedAtUtc", "Embedding")
            VALUES ({claim.ClaimId}, {options.EmbeddingModelId!}, {embedding.Length}, {vectorJson}, {hash}, {now}, CAST({vectorLiteral} AS vector))
            ON CONFLICT ("ClaimId") DO UPDATE SET
                "ModelId" = EXCLUDED."ModelId",
                "Dimensions" = EXCLUDED."Dimensions",
                "VectorJson" = EXCLUDED."VectorJson",
                "ContentHash" = EXCLUDED."ContentHash",
                "EmbeddedAtUtc" = EXCLUDED."EmbeddedAtUtc",
                "Embedding" = EXCLUDED."Embedding";
            """,
            cancellationToken);
        return true;
    }

    private static float[]? ParseVector(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<float[]>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static string BuildSearchText(SemanticClaimEntity claim)
        => $"{claim.Subject} {claim.Predicate} {claim.Value} {claim.Scope}".Trim();

    private static string ComputeContentHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.ToLowerInvariant()));
        return Convert.ToHexString(bytes);
    }

    private static string ToPgvectorLiteral(float[] vector)
        => $"[{string.Join(",", vector.Select(x => x.ToString("G9", System.Globalization.CultureInfo.InvariantCulture)))}]";

    private static double CosineSimilarity(float[] a, float[] b)
    {
        var length = Math.Min(a.Length, b.Length);
        if (length == 0)
        {
            return 0;
        }

        double dot = 0;
        double normA = 0;
        double normB = 0;
        for (var i = 0; i < length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        if (normA <= 0 || normB <= 0)
        {
            return 0;
        }

        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
