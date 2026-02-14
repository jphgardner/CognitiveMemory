using CognitiveMemory.Application.Contracts;
using CognitiveMemory.Domain.Entities;
using CognitiveMemory.Infrastructure.Persistence;
using CognitiveMemory.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace CognitiveMemory.Infrastructure.Tests;

public sealed class RepositoryInvariantTests
{
    [Fact]
    public async Task CreateClaimFailsWithoutEvidence()
    {
        await using var db = CreateDbContext();
        var repository = new ClaimRepository(db);

        var request = new CreateClaimRequest
        {
            SubjectEntityId = Guid.NewGuid(),
            Predicate = "selected_transport",
            LiteralValue = "SignalR",
            Hash = Guid.NewGuid().ToString("N")
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.CreateAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task ContradictionOnlyAllowsStatusAndResolutionNotesMutation()
    {
        await using var db = CreateDbContext();

        var contradiction = new Contradiction
        {
            ContradictionId = Guid.NewGuid(),
            ClaimAId = Guid.NewGuid(),
            ClaimBId = Guid.NewGuid(),
            Type = "Direct",
            Severity = "High",
            Status = "Open",
            DetectedAt = DateTimeOffset.UtcNow
        };

        db.Contradictions.Add(contradiction);
        await db.SaveChangesAsync();

        contradiction.Severity = "Low";

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ClaimLifecycleTransitionsArePersisted()
    {
        await using var db = CreateDbContext();
        var repository = new ClaimRepository(db);

        var claimA = await repository.CreateAsync(new CreateClaimRequest
        {
            SubjectEntityId = Guid.NewGuid(),
            Predicate = "selected_transport",
            LiteralValue = "SignalR",
            Hash = Guid.NewGuid().ToString("N"),
            Evidence = [new CreateEvidenceRequest { SourceRef = "c:1", ExcerptOrSummary = "SignalR was selected." }]
        }, CancellationToken.None);

        var claimB = await repository.CreateAsync(new CreateClaimRequest
        {
            SubjectEntityId = Guid.NewGuid(),
            Predicate = "selected_transport",
            LiteralValue = "WebSockets",
            Hash = Guid.NewGuid().ToString("N"),
            Evidence = [new CreateEvidenceRequest { SourceRef = "c:2", ExcerptOrSummary = "Alternative option." }]
        }, CancellationToken.None);

        var superseded = await repository.SupersedeAsync(claimA.ClaimId, claimB.ClaimId, CancellationToken.None);
        var retracted = await repository.RetractAsync(claimB.ClaimId, CancellationToken.None);

        Assert.Equal(ClaimStatus.Superseded, superseded.Status);
        Assert.Equal(ClaimStatus.Retracted, retracted.Status);
    }

    [Fact]
    public async Task CreatingConflictingClaimCreatesContradiction()
    {
        await using var db = CreateDbContext();
        var repository = new ClaimRepository(db);
        var subjectId = Guid.NewGuid();

        await repository.CreateAsync(new CreateClaimRequest
        {
            SubjectEntityId = subjectId,
            Predicate = "selected_transport",
            LiteralValue = "SignalR",
            Hash = Guid.NewGuid().ToString("N"),
            Evidence = [new CreateEvidenceRequest { SourceRef = "c:1", ExcerptOrSummary = "Selected SignalR." }]
        }, CancellationToken.None);

        await repository.CreateAsync(new CreateClaimRequest
        {
            SubjectEntityId = subjectId,
            Predicate = "selected_transport",
            LiteralValue = "WebSockets",
            Hash = Guid.NewGuid().ToString("N"),
            Evidence = [new CreateEvidenceRequest { SourceRef = "c:2", ExcerptOrSummary = "Selected WebSockets." }]
        }, CancellationToken.None);

        Assert.Single(db.Contradictions);
        Assert.Equal("Direct", db.Contradictions.Single().Type);
    }

    private static MemoryDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<MemoryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new MemoryDbContext(options);
    }
}
