
using Microsoft.EntityFrameworkCore;
using CognitiveMemory.Infrastructure.Persistence.Entities;

namespace CognitiveMemory.Infrastructure.Persistence;

public class MemoryDbContext(DbContextOptions<MemoryDbContext> options) : DbContext(options)
{
    public DbSet<EpisodicMemoryEventEntity> EpisodicMemoryEvents => Set<EpisodicMemoryEventEntity>();
    public DbSet<SemanticClaimEntity> SemanticClaims => Set<SemanticClaimEntity>();
    public DbSet<ClaimEvidenceEntity> ClaimEvidence => Set<ClaimEvidenceEntity>();
    public DbSet<ClaimContradictionEntity> ClaimContradictions => Set<ClaimContradictionEntity>();
    public DbSet<ConsolidationPromotionEntity> ConsolidationPromotions => Set<ConsolidationPromotionEntity>();
    public DbSet<ToolInvocationAuditEntity> ToolInvocationAudits => Set<ToolInvocationAuditEntity>();
    public DbSet<ProceduralRoutineEntity> ProceduralRoutines => Set<ProceduralRoutineEntity>();
    public DbSet<SelfPreferenceEntity> SelfPreferences => Set<SelfPreferenceEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EpisodicMemoryEventEntity>(entity =>
        {
            entity.ToTable("EpisodicMemoryEvents");
            entity.HasKey(x => x.EventId);

            entity.Property(x => x.SessionId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Who).HasMaxLength(128).IsRequired();
            entity.Property(x => x.What).IsRequired();
            entity.Property(x => x.Context).IsRequired();
            entity.Property(x => x.SourceReference).HasMaxLength(256).IsRequired();
            entity.Property(x => x.OccurredAt).IsRequired();

            entity.HasIndex(x => new { x.SessionId, x.OccurredAt });
        });

        modelBuilder.Entity<SemanticClaimEntity>(entity =>
        {
            entity.ToTable("SemanticClaims");
            entity.HasKey(x => x.ClaimId);

            entity.Property(x => x.Subject).HasMaxLength(256).IsRequired();
            entity.Property(x => x.Predicate).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Value).IsRequired();
            entity.Property(x => x.Confidence).IsRequired();
            entity.Property(x => x.Scope).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
            entity.Property(x => x.SupersededByClaimId);
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.Property(x => x.UpdatedAtUtc).IsRequired();

            entity.HasIndex(x => new { x.Subject, x.Predicate, x.Status });
            entity.HasIndex(x => x.SupersededByClaimId);
        });

        modelBuilder.Entity<ClaimEvidenceEntity>(entity =>
        {
            entity.ToTable("ClaimEvidence");
            entity.HasKey(x => x.EvidenceId);

            entity.Property(x => x.SourceType).HasMaxLength(64).IsRequired();
            entity.Property(x => x.SourceReference).HasMaxLength(256).IsRequired();
            entity.Property(x => x.ExcerptOrSummary).IsRequired();
            entity.Property(x => x.Strength).IsRequired();
            entity.Property(x => x.CapturedAtUtc).IsRequired();

            entity.HasIndex(x => new { x.ClaimId, x.CapturedAtUtc });
            entity.HasOne<SemanticClaimEntity>()
                .WithMany()
                .HasForeignKey(x => x.ClaimId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ClaimContradictionEntity>(entity =>
        {
            entity.ToTable("ClaimContradictions");
            entity.HasKey(x => x.ContradictionId);

            entity.Property(x => x.Type).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Severity).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
            entity.Property(x => x.DetectedAtUtc).IsRequired();

            entity.HasIndex(x => new { x.ClaimAId, x.ClaimBId }).IsUnique();
        });

        modelBuilder.Entity<ConsolidationPromotionEntity>(entity =>
        {
            entity.ToTable("ConsolidationPromotions");
            entity.HasKey(x => x.PromotionId);

            entity.Property(x => x.Outcome).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Notes);
            entity.Property(x => x.ProcessedAtUtc).IsRequired();

            entity.HasIndex(x => x.EpisodicEventId).IsUnique();
            entity.HasIndex(x => x.ProcessedAtUtc);
        });

        modelBuilder.Entity<ToolInvocationAuditEntity>(entity =>
        {
            entity.ToTable("ToolInvocationAudits");
            entity.HasKey(x => x.AuditId);

            entity.Property(x => x.ToolName).HasMaxLength(128).IsRequired();
            entity.Property(x => x.ArgumentsJson).IsRequired();
            entity.Property(x => x.ResultJson).IsRequired();
            entity.Property(x => x.Succeeded).IsRequired();
            entity.Property(x => x.Error);
            entity.Property(x => x.ExecutedAtUtc).IsRequired();

            entity.HasIndex(x => new { x.ToolName, x.ExecutedAtUtc });
        });

        modelBuilder.Entity<ProceduralRoutineEntity>(entity =>
        {
            entity.ToTable("ProceduralRoutines");
            entity.HasKey(x => x.RoutineId);

            entity.Property(x => x.Trigger).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(256).IsRequired();
            entity.Property(x => x.StepsJson).IsRequired();
            entity.Property(x => x.CheckpointsJson).IsRequired();
            entity.Property(x => x.Outcome).IsRequired();
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.Property(x => x.UpdatedAtUtc).IsRequired();

            entity.HasIndex(x => x.Trigger);
        });

        modelBuilder.Entity<SelfPreferenceEntity>(entity =>
        {
            entity.ToTable("SelfPreferences");
            entity.HasKey(x => x.Key);

            entity.Property(x => x.Key).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Value).IsRequired();
            entity.Property(x => x.UpdatedAtUtc).IsRequired();
        });
    }
}
