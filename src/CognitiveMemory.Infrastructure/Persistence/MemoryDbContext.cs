
using Microsoft.EntityFrameworkCore;
using CognitiveMemory.Infrastructure.Persistence.Entities;

namespace CognitiveMemory.Infrastructure.Persistence;

public class MemoryDbContext(DbContextOptions<MemoryDbContext> options) : DbContext(options)
{
    public DbSet<EpisodicMemoryEventEntity> EpisodicMemoryEvents => Set<EpisodicMemoryEventEntity>();
    public DbSet<SemanticClaimEntity> SemanticClaims => Set<SemanticClaimEntity>();
    public DbSet<SemanticClaimEmbeddingEntity> SemanticClaimEmbeddings => Set<SemanticClaimEmbeddingEntity>();
    public DbSet<ClaimEvidenceEntity> ClaimEvidence => Set<ClaimEvidenceEntity>();
    public DbSet<ClaimContradictionEntity> ClaimContradictions => Set<ClaimContradictionEntity>();
    public DbSet<ConsolidationPromotionEntity> ConsolidationPromotions => Set<ConsolidationPromotionEntity>();
    public DbSet<ToolInvocationAuditEntity> ToolInvocationAudits => Set<ToolInvocationAuditEntity>();
    public DbSet<ProceduralRoutineEntity> ProceduralRoutines => Set<ProceduralRoutineEntity>();
    public DbSet<SelfPreferenceEntity> SelfPreferences => Set<SelfPreferenceEntity>();
    public DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();
    public DbSet<ScheduledActionEntity> ScheduledActions => Set<ScheduledActionEntity>();
    public DbSet<EventConsumerCheckpointEntity> EventConsumerCheckpoints => Set<EventConsumerCheckpointEntity>();
    public DbSet<ConflictEscalationAlertEntity> ConflictEscalationAlerts => Set<ConflictEscalationAlertEntity>();
    public DbSet<UserProfileProjectionEntity> UserProfileProjections => Set<UserProfileProjectionEntity>();
    public DbSet<ProceduralRoutineMetricEntity> ProceduralRoutineMetrics => Set<ProceduralRoutineMetricEntity>();
    public DbSet<SubconsciousDebateSessionEntity> SubconsciousDebateSessions => Set<SubconsciousDebateSessionEntity>();
    public DbSet<SubconsciousDebateTurnEntity> SubconsciousDebateTurns => Set<SubconsciousDebateTurnEntity>();
    public DbSet<SubconsciousDebateOutcomeEntity> SubconsciousDebateOutcomes => Set<SubconsciousDebateOutcomeEntity>();
    public DbSet<SubconsciousDebateMetricEntity> SubconsciousDebateMetrics => Set<SubconsciousDebateMetricEntity>();
    public DbSet<MemoryRelationshipEntity> MemoryRelationships => Set<MemoryRelationshipEntity>();
    public DbSet<CompanionEntity> Companions => Set<CompanionEntity>();
    public DbSet<CompanionCognitiveProfileEntity> CompanionCognitiveProfiles => Set<CompanionCognitiveProfileEntity>();
    public DbSet<CompanionCognitiveProfileVersionEntity> CompanionCognitiveProfileVersions => Set<CompanionCognitiveProfileVersionEntity>();
    public DbSet<CompanionCognitiveProfileAuditEntity> CompanionCognitiveProfileAudits => Set<CompanionCognitiveProfileAuditEntity>();
    public DbSet<CompanionCognitiveRuntimeTraceEntity> CompanionCognitiveRuntimeTraces => Set<CompanionCognitiveRuntimeTraceEntity>();
    public DbSet<PortalUserEntity> PortalUsers => Set<PortalUserEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EpisodicMemoryEventEntity>(entity =>
        {
            entity.ToTable("EpisodicMemoryEvents");
            entity.HasKey(x => x.EventId);

            entity.Property(x => x.CompanionId).IsRequired();
            entity.Property(x => x.SessionId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Who).HasMaxLength(128).IsRequired();
            entity.Property(x => x.What).IsRequired();
            entity.Property(x => x.Context).IsRequired();
            entity.Property(x => x.SourceReference).HasMaxLength(256).IsRequired();
            entity.Property(x => x.OccurredAt).IsRequired();

            entity.HasIndex(x => new { x.CompanionId, x.SessionId, x.OccurredAt });
        });

        modelBuilder.Entity<SemanticClaimEntity>(entity =>
        {
            entity.ToTable("SemanticClaims");
            entity.HasKey(x => x.ClaimId);

            entity.Property(x => x.CompanionId).IsRequired();
            entity.Property(x => x.Subject).HasMaxLength(256).IsRequired();
            entity.Property(x => x.Predicate).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Value).IsRequired();
            entity.Property(x => x.Confidence).IsRequired();
            entity.Property(x => x.Scope).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
            entity.Property(x => x.SupersededByClaimId);
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.Property(x => x.UpdatedAtUtc).IsRequired();

            entity.HasIndex(x => new { x.CompanionId, x.Subject, x.Predicate, x.Status });
            entity.HasIndex(x => x.SupersededByClaimId);
        });

        modelBuilder.Entity<SemanticClaimEmbeddingEntity>(entity =>
        {
            entity.ToTable("SemanticClaimEmbeddings");
            entity.HasKey(x => x.ClaimId);

            entity.Property(x => x.ModelId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Dimensions).IsRequired();
            entity.Property(x => x.VectorJson).IsRequired();
            entity.Property(x => x.ContentHash).HasMaxLength(128).IsRequired();
            entity.Property(x => x.EmbeddedAtUtc).IsRequired();

            entity.HasIndex(x => x.EmbeddedAtUtc);
            entity.HasOne<SemanticClaimEntity>()
                .WithOne()
                .HasForeignKey<SemanticClaimEmbeddingEntity>(x => x.ClaimId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ClaimEvidenceEntity>(entity =>
        {
            entity.ToTable("ClaimEvidence");
            entity.HasKey(x => x.EvidenceId);

            entity.Property(x => x.CompanionId).IsRequired();
            entity.Property(x => x.SourceType).HasMaxLength(64).IsRequired();
            entity.Property(x => x.SourceReference).HasMaxLength(256).IsRequired();
            entity.Property(x => x.ExcerptOrSummary).IsRequired();
            entity.Property(x => x.Strength).IsRequired();
            entity.Property(x => x.CapturedAtUtc).IsRequired();

            entity.HasIndex(x => new { x.CompanionId, x.ClaimId, x.CapturedAtUtc });
            entity.HasOne<SemanticClaimEntity>()
                .WithMany()
                .HasForeignKey(x => x.ClaimId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ClaimContradictionEntity>(entity =>
        {
            entity.ToTable("ClaimContradictions");
            entity.HasKey(x => x.ContradictionId);

            entity.Property(x => x.CompanionId).IsRequired();
            entity.Property(x => x.Type).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Severity).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
            entity.Property(x => x.DetectedAtUtc).IsRequired();

            entity.HasIndex(x => new { x.CompanionId, x.ClaimAId, x.ClaimBId }).IsUnique();
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

            entity.Property(x => x.CompanionId).IsRequired();
            entity.Property(x => x.ToolName).HasMaxLength(128).IsRequired();
            entity.Property(x => x.ArgumentsJson).IsRequired();
            entity.Property(x => x.ResultJson).IsRequired();
            entity.Property(x => x.Succeeded).IsRequired();
            entity.Property(x => x.Error);
            entity.Property(x => x.ExecutedAtUtc).IsRequired();

            entity.HasIndex(x => new { x.CompanionId, x.ToolName, x.ExecutedAtUtc });
        });

        modelBuilder.Entity<ProceduralRoutineEntity>(entity =>
        {
            entity.ToTable("ProceduralRoutines");
            entity.HasKey(x => x.RoutineId);

            entity.Property(x => x.CompanionId).IsRequired();
            entity.Property(x => x.Trigger).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(256).IsRequired();
            entity.Property(x => x.StepsJson).IsRequired();
            entity.Property(x => x.CheckpointsJson).IsRequired();
            entity.Property(x => x.Outcome).IsRequired();
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.Property(x => x.UpdatedAtUtc).IsRequired();

            entity.HasIndex(x => new { x.CompanionId, x.Trigger });
        });

        modelBuilder.Entity<SelfPreferenceEntity>(entity =>
        {
            entity.ToTable("SelfPreferences");
            entity.HasKey(x => new { x.CompanionId, x.Key });

            entity.Property(x => x.CompanionId).IsRequired();
            entity.Property(x => x.Key).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Value).IsRequired();
            entity.Property(x => x.UpdatedAtUtc).IsRequired();
            entity.HasIndex(x => x.CompanionId);
        });

        modelBuilder.Entity<OutboxMessageEntity>(entity =>
        {
            entity.ToTable("OutboxMessages");
            entity.HasKey(x => x.EventId);

            entity.Property(x => x.EventType).HasMaxLength(128).IsRequired();
            entity.Property(x => x.AggregateType).HasMaxLength(128).IsRequired();
            entity.Property(x => x.AggregateId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.OccurredAtUtc).IsRequired();
            entity.Property(x => x.PayloadJson).IsRequired();
            entity.Property(x => x.HeadersJson).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
            entity.Property(x => x.RetryCount).IsRequired();
            entity.Property(x => x.LastError);
            entity.Property(x => x.LastAttemptedAtUtc);
            entity.Property(x => x.PublishedAtUtc);

            entity.HasIndex(x => new { x.Status, x.OccurredAtUtc });
            entity.HasIndex(x => x.AggregateId);
        });

        modelBuilder.Entity<ScheduledActionEntity>(entity =>
        {
            entity.ToTable("ScheduledActions");
            entity.HasKey(x => x.ActionId);

            entity.Property(x => x.CompanionId).IsRequired();
            entity.Property(x => x.SessionId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.ActionType).HasMaxLength(128).IsRequired();
            entity.Property(x => x.InputJson).IsRequired();
            entity.Property(x => x.RunAtUtc).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Attempts).IsRequired();
            entity.Property(x => x.MaxAttempts).IsRequired();
            entity.Property(x => x.LastError);
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.Property(x => x.UpdatedAtUtc).IsRequired();
            entity.Property(x => x.CompletedAtUtc);

            entity.HasIndex(x => new { x.CompanionId, x.Status, x.RunAtUtc });
            entity.HasIndex(x => new { x.CompanionId, x.SessionId, x.CreatedAtUtc });
        });

        modelBuilder.Entity<EventConsumerCheckpointEntity>(entity =>
        {
            entity.ToTable("EventConsumerCheckpoints");
            entity.HasKey(x => new { x.ConsumerName, x.EventId });

            entity.Property(x => x.ConsumerName).HasMaxLength(128).IsRequired();
            entity.Property(x => x.EventId).IsRequired();
            entity.Property(x => x.ProcessedAtUtc).IsRequired();

            entity.HasIndex(x => x.ProcessedAtUtc);
        });

        modelBuilder.Entity<ConflictEscalationAlertEntity>(entity =>
        {
            entity.ToTable("ConflictEscalationAlerts");
            entity.HasKey(x => x.AlertId);

            entity.Property(x => x.CompanionId).IsRequired();
            entity.Property(x => x.Subject).HasMaxLength(256).IsRequired();
            entity.Property(x => x.Predicate).HasMaxLength(128).IsRequired();
            entity.Property(x => x.ValuesJson).IsRequired();
            entity.Property(x => x.ContradictionCount).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.Property(x => x.LastSeenAtUtc).IsRequired();

            entity.HasIndex(x => new { x.CompanionId, x.Subject, x.Predicate, x.Status });
        });

        modelBuilder.Entity<UserProfileProjectionEntity>(entity =>
        {
            entity.ToTable("UserProfileProjections");
            entity.HasKey(x => new { x.CompanionId, x.Key });

            entity.Property(x => x.CompanionId).IsRequired();
            entity.Property(x => x.Key).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Value).IsRequired();
            entity.Property(x => x.Source).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Confidence).IsRequired();
            entity.Property(x => x.UpdatedAtUtc).IsRequired();

            entity.HasIndex(x => new { x.CompanionId, x.UpdatedAtUtc });
        });

        modelBuilder.Entity<ProceduralRoutineMetricEntity>(entity =>
        {
            entity.ToTable("ProceduralRoutineMetrics");
            entity.HasKey(x => x.RoutineId);

            entity.Property(x => x.CompanionId).IsRequired();
            entity.Property(x => x.Trigger).HasMaxLength(128).IsRequired();
            entity.Property(x => x.SuccessCount).IsRequired();
            entity.Property(x => x.FailureCount).IsRequired();
            entity.Property(x => x.LastOutcomeSummary);
            entity.Property(x => x.UpdatedAtUtc).IsRequired();

            entity.HasIndex(x => new { x.CompanionId, x.Trigger });
            entity.HasIndex(x => new { x.CompanionId, x.UpdatedAtUtc });
        });

        modelBuilder.Entity<SubconsciousDebateSessionEntity>(entity =>
        {
            entity.ToTable("SubconsciousDebateSessions");
            entity.HasKey(x => x.DebateId);

            entity.Property(x => x.CompanionId).IsRequired();
            entity.Property(x => x.SessionId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.TopicKey).HasMaxLength(128).IsRequired();
            entity.Property(x => x.TriggerEventType).HasMaxLength(128).IsRequired();
            entity.Property(x => x.TriggerPayloadJson).IsRequired();
            entity.Property(x => x.State).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Priority).IsRequired();
            entity.Property(x => x.LastError);
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.Property(x => x.UpdatedAtUtc).IsRequired();

            entity.HasIndex(x => new { x.CompanionId, x.SessionId, x.TopicKey, x.State });
            entity.HasIndex(x => new { x.CompanionId, x.SessionId, x.TopicKey, x.TriggerEventId })
                .IsUnique();
            entity.HasIndex(x => x.CreatedAtUtc);
            entity.HasIndex(x => x.TriggerEventId);
        });

        modelBuilder.Entity<SubconsciousDebateTurnEntity>(entity =>
        {
            entity.ToTable("SubconsciousDebateTurns");
            entity.HasKey(x => x.TurnId);

            entity.Property(x => x.CompanionId).IsRequired();
            entity.Property(x => x.AgentName).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Role).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Message).IsRequired();
            entity.Property(x => x.StructuredPayloadJson);
            entity.Property(x => x.Confidence);
            entity.Property(x => x.CreatedAtUtc).IsRequired();

            entity.HasIndex(x => new { x.CompanionId, x.DebateId, x.TurnNumber }).IsUnique();
            entity.HasIndex(x => new { x.CompanionId, x.CreatedAtUtc });
        });

        modelBuilder.Entity<SubconsciousDebateOutcomeEntity>(entity =>
        {
            entity.ToTable("SubconsciousDebateOutcomes");
            entity.HasKey(x => x.DebateId);

            entity.Property(x => x.CompanionId).IsRequired();
            entity.Property(x => x.OutcomeJson).IsRequired();
            entity.Property(x => x.OutcomeHash).HasMaxLength(128).IsRequired();
            entity.Property(x => x.ValidationStatus).HasMaxLength(32).IsRequired();
            entity.Property(x => x.ApplyStatus).HasMaxLength(32).IsRequired();
            entity.Property(x => x.ApplyError);
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.Property(x => x.UpdatedAtUtc).IsRequired();
            entity.Property(x => x.AppliedAtUtc);

            entity.HasIndex(x => new { x.CompanionId, x.OutcomeHash });
        });

        modelBuilder.Entity<SubconsciousDebateMetricEntity>(entity =>
        {
            entity.ToTable("SubconsciousDebateMetrics");
            entity.HasKey(x => x.DebateId);

            entity.Property(x => x.CompanionId).IsRequired();
            entity.Property(x => x.TurnCount).IsRequired();
            entity.Property(x => x.DurationMs).IsRequired();
            entity.Property(x => x.ConvergenceScore).IsRequired();
            entity.Property(x => x.ContradictionsDetected).IsRequired();
            entity.Property(x => x.ClaimsProposed).IsRequired();
            entity.Property(x => x.ClaimsApplied).IsRequired();
            entity.Property(x => x.RequiresUserInput).IsRequired();
            entity.Property(x => x.FinalConfidence).IsRequired();
            entity.Property(x => x.CreatedAtUtc).IsRequired();

            entity.HasIndex(x => new { x.CompanionId, x.CreatedAtUtc });
        });

        modelBuilder.Entity<MemoryRelationshipEntity>(entity =>
        {
            entity.ToTable("MemoryRelationships");
            entity.HasKey(x => x.RelationshipId);

            entity.Property(x => x.CompanionId).IsRequired();
            entity.Property(x => x.SessionId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.FromType).HasMaxLength(64).IsRequired();
            entity.Property(x => x.FromId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.ToType).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ToId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.RelationshipType).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Confidence).IsRequired();
            entity.Property(x => x.Strength).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
            entity.Property(x => x.ValidFromUtc);
            entity.Property(x => x.ValidToUtc);
            entity.Property(x => x.MetadataJson);
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.Property(x => x.UpdatedAtUtc).IsRequired();

            entity.HasIndex(x => new { x.CompanionId, x.SessionId, x.RelationshipType, x.Status });
            entity.HasIndex(x => new { x.CompanionId, x.SessionId, x.FromType, x.FromId, x.Status });
            entity.HasIndex(x => new { x.CompanionId, x.SessionId, x.ToType, x.ToId, x.Status });
            entity.HasIndex(x => new { x.CompanionId, x.SessionId, x.FromType, x.FromId, x.ToType, x.ToId, x.RelationshipType })
                .IsUnique();
        });

        modelBuilder.Entity<CompanionEntity>(entity =>
        {
            entity.ToTable("Companions");
            entity.HasKey(x => x.CompanionId);

            entity.Property(x => x.UserId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Tone).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Purpose).IsRequired();
            entity.Property(x => x.ModelHint).HasMaxLength(128).IsRequired();
            entity.Property(x => x.SessionId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.OriginStory).IsRequired();
            entity.Property(x => x.BirthDateUtc);
            entity.Property(x => x.InitialMemoryText);
            entity.Property(x => x.ActiveCognitiveProfileVersionId);
            entity.Property(x => x.MetadataJson).IsRequired();
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.Property(x => x.UpdatedAtUtc).IsRequired();
            entity.Property(x => x.IsArchived).IsRequired();

            entity.HasIndex(x => new { x.UserId, x.UpdatedAtUtc });
            entity.HasIndex(x => new { x.UserId, x.Name });
            entity.HasIndex(x => x.SessionId).IsUnique();
            entity.HasIndex(x => x.ActiveCognitiveProfileVersionId);
        });

        modelBuilder.Entity<CompanionCognitiveProfileEntity>(entity =>
        {
            entity.ToTable("CompanionCognitiveProfiles");
            entity.HasKey(x => x.CompanionId);

            entity.Property(x => x.CompanionId).IsRequired();
            entity.Property(x => x.ActiveProfileVersionId).IsRequired();
            entity.Property(x => x.StagedProfileVersionId);
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.Property(x => x.UpdatedAtUtc).IsRequired();
            entity.Property(x => x.UpdatedByUserId).HasMaxLength(128).IsRequired();

            entity.HasIndex(x => x.ActiveProfileVersionId);
            entity.HasIndex(x => x.StagedProfileVersionId);
            entity.HasIndex(x => x.UpdatedAtUtc);

            entity.HasOne<CompanionEntity>()
                .WithMany()
                .HasForeignKey(x => x.CompanionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CompanionCognitiveProfileVersionEntity>(entity =>
        {
            entity.ToTable("CompanionCognitiveProfileVersions");
            entity.HasKey(x => x.ProfileVersionId);

            entity.Property(x => x.ProfileVersionId).IsRequired();
            entity.Property(x => x.CompanionId).IsRequired();
            entity.Property(x => x.VersionNumber).IsRequired();
            entity.Property(x => x.SchemaVersion).HasMaxLength(32).IsRequired();
            entity.Property(x => x.ProfileJson).IsRequired();
            entity.Property(x => x.CompiledRuntimeJson).IsRequired();
            entity.Property(x => x.ProfileHash).HasMaxLength(128).IsRequired();
            entity.Property(x => x.ValidationStatus).HasMaxLength(32).IsRequired();
            entity.Property(x => x.CreatedByUserId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.ChangeSummary);
            entity.Property(x => x.ChangeReason);
            entity.Property(x => x.CreatedAtUtc).IsRequired();

            entity.HasIndex(x => new { x.CompanionId, x.VersionNumber }).IsUnique();
            entity.HasIndex(x => new { x.CompanionId, x.ProfileHash }).IsUnique();
            entity.HasIndex(x => new { x.CompanionId, x.CreatedAtUtc });
            entity.HasIndex(x => new { x.CompanionId, x.ValidationStatus, x.CreatedAtUtc });

            entity.HasOne<CompanionEntity>()
                .WithMany()
                .HasForeignKey(x => x.CompanionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CompanionCognitiveProfileAuditEntity>(entity =>
        {
            entity.ToTable("CompanionCognitiveProfileAudits");
            entity.HasKey(x => x.AuditId);

            entity.Property(x => x.AuditId).IsRequired();
            entity.Property(x => x.CompanionId).IsRequired();
            entity.Property(x => x.ActorUserId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Action).HasMaxLength(64).IsRequired();
            entity.Property(x => x.FromProfileVersionId);
            entity.Property(x => x.ToProfileVersionId);
            entity.Property(x => x.DiffJson).IsRequired();
            entity.Property(x => x.Reason);
            entity.Property(x => x.CreatedAtUtc).IsRequired();

            entity.HasIndex(x => new { x.CompanionId, x.CreatedAtUtc });
            entity.HasIndex(x => new { x.CompanionId, x.Action, x.CreatedAtUtc });

            entity.HasOne<CompanionEntity>()
                .WithMany()
                .HasForeignKey(x => x.CompanionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CompanionCognitiveRuntimeTraceEntity>(entity =>
        {
            entity.ToTable("CompanionCognitiveRuntimeTraces");
            entity.HasKey(x => x.TraceId);

            entity.Property(x => x.TraceId).IsRequired();
            entity.Property(x => x.CompanionId).IsRequired();
            entity.Property(x => x.SessionId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.ProfileVersionId).IsRequired();
            entity.Property(x => x.RequestCorrelationId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Phase).HasMaxLength(64).IsRequired();
            entity.Property(x => x.DecisionJson).IsRequired();
            entity.Property(x => x.LatencyMs).IsRequired();
            entity.Property(x => x.CreatedAtUtc).IsRequired();

            entity.HasIndex(x => new { x.CompanionId, x.CreatedAtUtc });
            entity.HasIndex(x => new { x.CompanionId, x.SessionId, x.CreatedAtUtc });
            entity.HasIndex(x => new { x.CompanionId, x.ProfileVersionId, x.CreatedAtUtc });
            entity.HasIndex(x => new { x.CompanionId, x.RequestCorrelationId, x.CreatedAtUtc });

            entity.HasOne<CompanionEntity>()
                .WithMany()
                .HasForeignKey(x => x.CompanionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PortalUserEntity>(entity =>
        {
            entity.ToTable("PortalUsers");
            entity.HasKey(x => x.UserId);

            entity.Property(x => x.Email).HasMaxLength(256).IsRequired();
            entity.Property(x => x.PasswordHash).HasMaxLength(512).IsRequired();
            entity.Property(x => x.PasswordSalt).HasMaxLength(512).IsRequired();
            entity.Property(x => x.CreatedAtUtc).IsRequired();
            entity.Property(x => x.UpdatedAtUtc).IsRequired();
            entity.Property(x => x.IsActive).IsRequired();

            entity.HasIndex(x => x.Email).IsUnique();
            entity.HasIndex(x => x.UpdatedAtUtc);
        });
    }
}
