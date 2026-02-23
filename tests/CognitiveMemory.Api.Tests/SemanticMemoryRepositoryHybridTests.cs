using CognitiveMemory.Domain.Memory;
using CognitiveMemory.Infrastructure.Events;
using CognitiveMemory.Infrastructure.Companions;
using CognitiveMemory.Infrastructure.Persistence;
using CognitiveMemory.Infrastructure.Persistence.Entities;
using CognitiveMemory.Infrastructure.Repositories;
using CognitiveMemory.Infrastructure.SemanticKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CognitiveMemory.Api.Tests;

public sealed class SemanticMemoryRepositoryHybridTests
{
    [Fact(Skip = "Repository retrieval paths are PostgreSQL-specific (ILIKE/pgvector). Run this as integration against PostgreSQL.")]
    public async Task SearchClaimsAsync_VectorRecall_FindsClaimWhenLexicalMisses()
    {
        await using var db = CreateDb();
        var repository = CreateRepository(db);
        var companionId = Guid.NewGuid();

        var now = DateTimeOffset.UtcNow;
        await repository.CreateClaimAsync(
            companionId,
            new SemanticClaim(
                Guid.NewGuid(),
                "session:s1",
                "favorite_color",
                "blue",
                0.95,
                "global",
                SemanticClaimStatus.Active,
                null,
                null,
                null,
                now,
                now));

        var results = await repository.SearchClaimsAsync(companionId, "azure", take: 5);

        Assert.NotEmpty(results);
        Assert.Equal("blue", results[0].Value);
    }

    [Fact(Skip = "Repository retrieval paths are PostgreSQL-specific (ILIKE/pgvector). Run this as integration against PostgreSQL.")]
    public async Task SearchClaimsAsync_LazyBackfill_CreatesMissingEmbeddings()
    {
        await using var db = CreateDb();
        var repository = CreateRepository(db);
        var companionId = Guid.NewGuid();
        var claimId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        db.SemanticClaims.Add(
            new SemanticClaimEntity
            {
                ClaimId = claimId,
                CompanionId = companionId,
                Subject = "session:s1",
                Predicate = "origin",
                Value = "Neo Tokyo",
                Confidence = 0.8,
                Scope = "global",
                Status = SemanticClaimStatus.Active.ToString(),
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        await db.SaveChangesAsync();

        var results = await repository.SearchClaimsAsync(companionId, "origin", take: 5);

        Assert.NotEmpty(results);
        var embedding = await db.SemanticClaimEmbeddings.AsNoTracking().FirstOrDefaultAsync(x => x.ClaimId == claimId);
        Assert.NotNull(embedding);
        Assert.True(embedding!.Dimensions > 0);
    }

    private static MemoryDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<MemoryDbContext>()
            .UseInMemoryDatabase($"semantic-hybrid-tests-{Guid.NewGuid():N}")
            .Options;
        return new MemoryDbContext(options);
    }

    private static SemanticMemoryRepository CreateRepository(MemoryDbContext db)
    {
        var options = new SemanticKernelOptions
        {
            Provider = "Ollama",
            EmbeddingProvider = "Ollama",
            EmbeddingModelId = "test-embed",
            EnableVectorSearch = true,
            LexicalCandidateMultiplier = 6,
            VectorCandidateMultiplier = 6,
            HybridRrfK = 60,
            MaxVectorCandidatePool = 500,
            LazyEmbeddingBackfillTake = 16
        };

        return new SemanticMemoryRepository(
            db,
            new NoopOutboxWriter(),
            new TestCompanionScopeResolver(),
            new TestEmbeddingGenerator(),
            options,
            NullLogger<SemanticMemoryRepository>.Instance);
    }

    private sealed class NoopOutboxWriter : IOutboxWriter
    {
        public void Enqueue(string eventType, string aggregateType, string aggregateId, object payload, object? headers = null)
        {
        }
    }

    private sealed class TestEmbeddingGenerator : ITextEmbeddingGenerator
    {
        public Task<float[]?> GenerateAsync(string text, CancellationToken cancellationToken = default)
        {
            var normalized = text.Trim().ToLowerInvariant();
            if (normalized.Contains("azure", StringComparison.Ordinal) || normalized.Contains("blue", StringComparison.Ordinal))
            {
                return Task.FromResult<float[]?>([1f, 0f]);
            }

            if (normalized.Contains("green", StringComparison.Ordinal))
            {
                return Task.FromResult<float[]?>([0f, 1f]);
            }

            return Task.FromResult<float[]?>([0.2f, 0.2f]);
        }
    }

    private sealed class TestCompanionScopeResolver : ICompanionScopeResolver
    {
        public Task<Guid?> TryResolveCompanionIdAsync(string sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult<Guid?>(Guid.NewGuid());

        public Task<Guid> ResolveCompanionIdOrThrowAsync(string sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult(Guid.NewGuid());
    }
}
