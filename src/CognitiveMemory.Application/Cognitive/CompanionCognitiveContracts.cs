using System.Text.Json;

namespace CognitiveMemory.Application.Cognitive;

public sealed class CompanionCognitiveProfileDocument
{
    public string SchemaVersion { get; set; } = "1.0.0";
    public AttentionControl Attention { get; set; } = new();
    public MemoryControl Memory { get; set; } = new();
    public ReasoningControl Reasoning { get; set; } = new();
    public ExpressionControl Expression { get; set; } = new();
    public ReflectionControl Reflection { get; set; } = new();
    public UncertaintyControl Uncertainty { get; set; } = new();
    public AdaptationControl Adaptation { get; set; } = new();
    public EvolutionControl Evolution { get; set; } = new();
}

public sealed class AttentionControl
{
    public double FocusStickiness { get; set; } = 0.65;
    public int ExplorationBreadth { get; set; } = 2;
    public double ClarificationFrequency { get; set; } = 0.2;
    public ContextWindowAllocation ContextWindowAllocation { get; set; } = new();
}

public sealed class ContextWindowAllocation
{
    public double Working { get; set; } = 0.34;
    public double Episodic { get; set; } = 0.2;
    public double Semantic { get; set; } = 0.24;
    public double Procedural { get; set; } = 0.12;
    public double Self { get; set; } = 0.1;
}

public sealed class MemoryControl
{
    public RetrievalWeighting RetrievalWeights { get; set; } = new();
    public LayerPriorityOverrides LayerPriorities { get; set; } = new();
    public int MaxCandidates { get; set; } = 120;
    public int MaxResults { get; set; } = 20;
    public double DedupeSensitivity { get; set; } = 0.6;
    public WriteThresholds WriteThresholds { get; set; } = new();
    public DecayPolicy Decay { get; set; } = new();
}

public sealed class RetrievalWeighting
{
    public double Recency { get; set; } = 0.8;
    public double SemanticMatch { get; set; } = 1.0;
    public double EvidenceStrength { get; set; } = 0.7;
    public double RelationshipDegree { get; set; } = 0.45;
    public double Confidence { get; set; } = 0.65;
}

public sealed class LayerPriorityOverrides
{
    public double Working { get; set; } = 0.2;
    public double Episodic { get; set; } = 0.4;
    public double Semantic { get; set; } = 0.6;
    public double Procedural { get; set; } = 0.45;
    public double Self { get; set; } = 0.5;
    public double IdentityBoost { get; set; } = 0.9;
}

public sealed class WriteThresholds
{
    public double ConfidenceMin { get; set; } = 0.62;
    public double ImportanceMin { get; set; } = 0.55;
}

public sealed class DecayPolicy
{
    public double SemanticDailyDecay { get; set; } = 0.02;
    public double EpisodicDailyDecay { get; set; } = 0.04;
    public double ReinforcementMultiplier { get; set; } = 1.2;
}

public sealed class ReasoningControl
{
    public string ReasoningMode { get; set; } = "hybrid";
    public string StructureTemplate { get; set; } = "evidence-first";
    public int Depth { get; set; } = 2;
    public double EvidenceStrictness { get; set; } = 0.7;
}

public sealed class ExpressionControl
{
    public string VerbosityTarget { get; set; } = "balanced";
    public string ToneStyle { get; set; } = "professional";
    public double EmotionalExpressivity { get; set; } = 0.2;
    public double FormatRigidity { get; set; } = 0.55;
}

public sealed class ReflectionControl
{
    public bool SelfCritiqueEnabled { get; set; } = true;
    public double SelfCritiqueRate { get; set; } = 0.25;
    public int MaxSelfCritiquePasses { get; set; } = 1;
    public DebatePolicy Debate { get; set; } = new();
}

public sealed class DebatePolicy
{
    public double TriggerSensitivity { get; set; } = 0.55;
    public int TurnCap { get; set; } = 8;
    public double TerminationConfidenceThreshold { get; set; } = 0.78;
    public double ConvergenceDeltaMin { get; set; } = 0.02;
}

public sealed class UncertaintyControl
{
    public double AnswerConfidenceThreshold { get; set; } = 0.66;
    public double ClarifyConfidenceThreshold { get; set; } = 0.5;
    public double DeferConfidenceThreshold { get; set; } = 0.3;
    public double ConflictEscalationThreshold { get; set; } = 0.74;
    public bool RequireCitationsInHighRiskDomains { get; set; } = true;
}

public sealed class AdaptationControl
{
    public double Procedurality { get; set; } = 0.58;
    public double Adaptivity { get; set; } = 0.42;
    public double PolicyStrictness { get; set; } = 0.65;
}

public sealed class EvolutionControl
{
    public string EvolutionMode { get; set; } = "propose-only";
    public double MaxDailyDelta { get; set; } = 0.06;
    public LearningSignals LearningSignals { get; set; } = new();
    public string ApprovalPolicy { get; set; } = "human-required";
}

public sealed class LearningSignals
{
    public bool UserSatisfaction { get; set; } = true;
    public bool HallucinationDetections { get; set; } = true;
    public bool ClarificationRate { get; set; } = true;
    public bool LatencyBreaches { get; set; } = true;
}

public sealed record CompanionCognitiveRuntimePolicy(
    Guid CompanionId,
    Guid ProfileVersionId,
    int VersionNumber,
    CompanionCognitiveProfileDocument Profile,
    RuntimeLimits Limits);

public sealed record RuntimeLimits(
    int MaxRetrieveCandidates,
    int MaxRetrieveResults,
    int MaxDebateTurns,
    int MaxSelfCritiquePasses);

public sealed record ResolvedCompanionCognitiveProfile(
    Guid CompanionId,
    Guid ProfileVersionId,
    int VersionNumber,
    CompanionCognitiveProfileDocument Profile,
    CompanionCognitiveRuntimePolicy RuntimePolicy,
    bool IsFallback);

public sealed record CompanionCognitiveProfileState(
    Guid CompanionId,
    Guid ActiveProfileVersionId,
    Guid? StagedProfileVersionId,
    int ActiveVersionNumber,
    string SchemaVersion,
    string ValidationStatus,
    DateTimeOffset UpdatedAtUtc,
    string UpdatedByUserId);

public sealed record CompanionCognitiveProfileVersion(
    Guid ProfileVersionId,
    Guid CompanionId,
    int VersionNumber,
    string SchemaVersion,
    string ValidationStatus,
    string ProfileHash,
    string CreatedByUserId,
    string? ChangeSummary,
    string? ChangeReason,
    DateTimeOffset CreatedAtUtc,
    string ProfileJson,
    string CompiledRuntimeJson);

public sealed record CompanionCognitiveProfileAudit(
    Guid AuditId,
    Guid CompanionId,
    string ActorUserId,
    string Action,
    Guid? FromProfileVersionId,
    Guid? ToProfileVersionId,
    string DiffJson,
    string? Reason,
    DateTimeOffset CreatedAtUtc);

public sealed record CompanionCognitiveRuntimeTrace(
    Guid TraceId,
    Guid CompanionId,
    string SessionId,
    Guid ProfileVersionId,
    string RequestCorrelationId,
    string Phase,
    string DecisionJson,
    int LatencyMs,
    DateTimeOffset CreatedAtUtc);

public sealed record CompanionCognitiveProfileValidationResult(
    bool IsValid,
    string[] Errors,
    string[] Warnings,
    CompanionCognitiveProfileDocument? NormalizedProfile);

public sealed record CreateCompanionCognitiveProfileVersionRequest(
    Guid CompanionId,
    string ActorUserId,
    CompanionCognitiveProfileDocument Profile,
    string? ChangeSummary,
    string? ChangeReason,
    bool ValidateOnly = false);

public sealed record StageCompanionCognitiveProfileRequest(
    Guid CompanionId,
    Guid ProfileVersionId,
    string ActorUserId,
    string? Reason);

public sealed record ActivateCompanionCognitiveProfileRequest(
    Guid CompanionId,
    Guid ProfileVersionId,
    string ActorUserId,
    string? Reason);

public sealed record RollbackCompanionCognitiveProfileRequest(
    Guid CompanionId,
    Guid TargetProfileVersionId,
    string ActorUserId,
    string? Reason);

public sealed record SimulateCompanionCognitiveProfileRequest(
    Guid CompanionId,
    string SessionId,
    CompanionCognitiveProfileDocument Profile,
    string Query);

public sealed record SimulateCompanionCognitiveProfileResult(
    CompanionCognitiveProfileValidationResult Validation,
    string[] SelectedLayers,
    JsonElement RetrievalWeighting,
    JsonElement RuntimeLimits);
