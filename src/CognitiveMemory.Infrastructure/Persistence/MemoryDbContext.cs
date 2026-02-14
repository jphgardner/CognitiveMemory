using CognitiveMemory.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CognitiveMemory.Infrastructure.Persistence;

public class MemoryDbContext(DbContextOptions<MemoryDbContext> options) : DbContext(options)
{
    public DbSet<MemoryEntity> Entities => Set<MemoryEntity>();

    public DbSet<Claim> Claims => Set<Claim>();

    public DbSet<Evidence> Evidence => Set<Evidence>();

    public DbSet<Contradiction> Contradictions => Set<Contradiction>();

    public DbSet<SourceDocument> Documents => Set<SourceDocument>();

    public DbSet<ToolExecution> ToolExecutions => Set<ToolExecution>();

    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();

    public DbSet<PolicyDecision> PolicyDecisions => Set<PolicyDecision>();

    public DbSet<ClaimInsight> ClaimInsights => Set<ClaimInsight>();

    public DbSet<ClaimCalibration> ClaimCalibrations => Set<ClaimCalibration>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MemoryEntity>()
            .Property(e => e.Aliases)
            .HasColumnType("jsonb");
        modelBuilder.Entity<MemoryEntity>()
            .HasIndex(e => new { e.Type, e.Name })
            .IsUnique();

        modelBuilder.Entity<Claim>()
            .Property(c => c.Status)
            .HasConversion<string>();
        modelBuilder.Entity<Claim>()
            .HasIndex(c => c.Hash)
            .IsUnique();

        modelBuilder.Entity<Claim>()
            .ToTable(tableBuilder =>
                tableBuilder.HasCheckConstraint("ck_claim_object_or_literal", "(\"ObjectEntityId\" IS NULL) <> (\"LiteralValue\" IS NULL)"));

        modelBuilder.Entity<Evidence>()
            .HasOne(e => e.Claim)
            .WithMany(c => c.Evidence)
            .HasForeignKey(e => e.ClaimId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SourceDocument>()
            .HasIndex(d => new { d.SourceType, d.SourceRef, d.ContentHash })
            .IsUnique();

        modelBuilder.Entity<Contradiction>()
            .HasIndex(c => new { c.ClaimAId, c.ClaimBId })
            .IsUnique();

        modelBuilder.Entity<ToolExecution>()
            .HasIndex(x => new { x.ToolName, x.IdempotencyKey })
            .IsUnique();

        modelBuilder.Entity<OutboxEvent>()
            .HasIndex(x => new { x.EventType, x.IdempotencyKey })
            .IsUnique();
        modelBuilder.Entity<OutboxEvent>()
            .HasIndex(x => new { x.Status, x.AvailableAt });

        modelBuilder.Entity<PolicyDecision>()
            .HasIndex(x => new { x.SourceType, x.SourceRef, x.CreatedAt });

        modelBuilder.Entity<ClaimInsight>()
            .HasIndex(x => x.UpdatedAt);

        modelBuilder.Entity<ClaimCalibration>()
            .HasIndex(x => new { x.ClaimId, x.CreatedAt });
    }

    public override int SaveChanges()
    {
        EnforceContradictionMutability();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        EnforceContradictionMutability();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void EnforceContradictionMutability()
    {
        var modifiedContradictions = ChangeTracker.Entries<Contradiction>()
            .Where(e => e.State == EntityState.Modified);

        foreach (var entry in modifiedContradictions)
        {
            foreach (var property in entry.Properties.Where(p => p.IsModified))
            {
                if (property.Metadata.Name is nameof(Contradiction.Status) or nameof(Contradiction.ResolutionNotes))
                {
                    continue;
                }

                throw new InvalidOperationException("Contradiction rows are immutable except for Status and ResolutionNotes.");
            }
        }
    }
}
