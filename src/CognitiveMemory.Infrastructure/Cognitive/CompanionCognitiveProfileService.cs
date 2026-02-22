using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CognitiveMemory.Application.Cognitive;
using CognitiveMemory.Infrastructure.Companions;
using CognitiveMemory.Infrastructure.Persistence;
using CognitiveMemory.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CognitiveMemory.Infrastructure.Cognitive;

public sealed class CompanionCognitiveProfileService(
    MemoryDbContext dbContext,
    ICompanionScopeResolver companionScopeResolver,
    ILogger<CompanionCognitiveProfileService> logger)
    : ICompanionCognitiveProfileService, ICompanionCognitiveProfileResolver, ICompanionCognitiveRuntimeTraceService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string CurrentSchemaVersion = "1.0.0";

    public async Task<CompanionCognitiveProfileState> GetStateAsync(Guid companionId, CancellationToken cancellationToken = default)
    {
        var state = await dbContext.CompanionCognitiveProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CompanionId == companionId, cancellationToken);
        if (state is null)
        {
            throw new InvalidOperationException($"No cognitive profile exists for companion '{companionId:D}'.");
        }

        var activeVersion = await dbContext.CompanionCognitiveProfileVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ProfileVersionId == state.ActiveProfileVersionId, cancellationToken)
            ?? throw new InvalidOperationException($"Active profile version '{state.ActiveProfileVersionId:D}' is missing.");

        return new CompanionCognitiveProfileState(
            state.CompanionId,
            state.ActiveProfileVersionId,
            state.StagedProfileVersionId,
            activeVersion.VersionNumber,
            activeVersion.SchemaVersion,
            activeVersion.ValidationStatus,
            state.UpdatedAtUtc,
            state.UpdatedByUserId);
    }

    public async Task<IReadOnlyList<CompanionCognitiveProfileVersion>> GetVersionsAsync(Guid companionId, int take = 50, CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.CompanionCognitiveProfileVersions
            .AsNoTracking()
            .Where(x => x.CompanionId == companionId)
            .OrderByDescending(x => x.VersionNumber)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(cancellationToken);

        return rows.Select(ToVersion).ToArray();
    }

    public Task<CompanionCognitiveProfileValidationResult> ValidateAsync(CompanionCognitiveProfileDocument profile, CancellationToken cancellationToken = default)
        => Task.FromResult(CompanionCognitiveProfileValidation.Validate(profile));

    public async Task<CompanionCognitiveProfileVersion> CreateVersionAsync(
        CreateCompanionCognitiveProfileVersionRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalizedActor = NormalizeActor(request.ActorUserId);
        var validation = CompanionCognitiveProfileValidation.Validate(request.Profile);
        if (!validation.IsValid || validation.NormalizedProfile is null)
        {
            throw new InvalidOperationException($"Invalid cognitive profile: {string.Join("; ", validation.Errors)}");
        }

        var companion = await dbContext.Companions.FirstOrDefaultAsync(x => x.CompanionId == request.CompanionId, cancellationToken)
                        ?? throw new InvalidOperationException($"Companion '{request.CompanionId:D}' not found.");

        var now = DateTimeOffset.UtcNow;
        var maxVersion = await dbContext.CompanionCognitiveProfileVersions
            .Where(x => x.CompanionId == request.CompanionId)
            .Select(x => (int?)x.VersionNumber)
            .MaxAsync(cancellationToken);

        var nextVersionNumber = (maxVersion ?? 0) + 1;
        var profileVersionId = Guid.NewGuid();
        var runtime = BuildRuntimePolicy(request.CompanionId, profileVersionId, nextVersionNumber, validation.NormalizedProfile);
        var profileJson = JsonSerializer.Serialize(validation.NormalizedProfile, JsonOptions);
        var runtimeJson = JsonSerializer.Serialize(runtime, JsonOptions);
        var profileHash = ComputeHash(profileJson);

        var version = new CompanionCognitiveProfileVersionEntity
        {
            ProfileVersionId = profileVersionId,
            CompanionId = request.CompanionId,
            VersionNumber = nextVersionNumber,
            SchemaVersion = CurrentSchemaVersion,
            ProfileJson = profileJson,
            CompiledRuntimeJson = runtimeJson,
            ProfileHash = profileHash,
            ValidationStatus = "Validated",
            CreatedByUserId = normalizedActor,
            ChangeSummary = request.ChangeSummary?.Trim(),
            ChangeReason = request.ChangeReason?.Trim(),
            CreatedAtUtc = now
        };
        dbContext.CompanionCognitiveProfileVersions.Add(version);

        var state = await dbContext.CompanionCognitiveProfiles.FirstOrDefaultAsync(x => x.CompanionId == request.CompanionId, cancellationToken);
        if (state is null)
        {
            state = new CompanionCognitiveProfileEntity
            {
                CompanionId = request.CompanionId,
                ActiveProfileVersionId = profileVersionId,
                StagedProfileVersionId = null,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                UpdatedByUserId = normalizedActor
            };
            dbContext.CompanionCognitiveProfiles.Add(state);
            companion.ActiveCognitiveProfileVersionId = profileVersionId;
            version.ValidationStatus = "Active";

            dbContext.CompanionCognitiveProfileAudits.Add(
                new CompanionCognitiveProfileAuditEntity
                {
                    AuditId = Guid.NewGuid(),
                    CompanionId = request.CompanionId,
                    ActorUserId = normalizedActor,
                    Action = "CreateVersion",
                    FromProfileVersionId = null,
                    ToProfileVersionId = profileVersionId,
                    DiffJson = "{}",
                    Reason = request.ChangeReason?.Trim() ?? "Initial profile bootstrap.",
                    CreatedAtUtc = now
                });
        }
        else
        {
            state.StagedProfileVersionId = profileVersionId;
            state.UpdatedAtUtc = now;
            state.UpdatedByUserId = normalizedActor;
            dbContext.CompanionCognitiveProfileAudits.Add(
                new CompanionCognitiveProfileAuditEntity
                {
                    AuditId = Guid.NewGuid(),
                    CompanionId = request.CompanionId,
                    ActorUserId = normalizedActor,
                    Action = request.ValidateOnly ? "Validate" : "CreateVersion",
                    FromProfileVersionId = state.ActiveProfileVersionId,
                    ToProfileVersionId = profileVersionId,
                    DiffJson = "{}",
                    Reason = request.ChangeReason?.Trim(),
                    CreatedAtUtc = now
                });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return ToVersion(version);
    }

    public async Task<CompanionCognitiveProfileState> StageAsync(StageCompanionCognitiveProfileRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedActor = NormalizeActor(request.ActorUserId);
        var state = await dbContext.CompanionCognitiveProfiles.FirstOrDefaultAsync(x => x.CompanionId == request.CompanionId, cancellationToken)
                    ?? throw new InvalidOperationException($"No profile state exists for companion '{request.CompanionId:D}'.");

        _ = await ResolveVersionOrThrowAsync(request.CompanionId, request.ProfileVersionId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        state.StagedProfileVersionId = request.ProfileVersionId;
        state.UpdatedAtUtc = now;
        state.UpdatedByUserId = normalizedActor;
        dbContext.CompanionCognitiveProfileAudits.Add(
            new CompanionCognitiveProfileAuditEntity
            {
                AuditId = Guid.NewGuid(),
                CompanionId = request.CompanionId,
                ActorUserId = normalizedActor,
                Action = "Stage",
                FromProfileVersionId = state.ActiveProfileVersionId,
                ToProfileVersionId = request.ProfileVersionId,
                DiffJson = "{}",
                Reason = request.Reason?.Trim(),
                CreatedAtUtc = now
            });

        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetStateAsync(request.CompanionId, cancellationToken);
    }

    public async Task<CompanionCognitiveProfileState> ActivateAsync(ActivateCompanionCognitiveProfileRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedActor = NormalizeActor(request.ActorUserId);
        var state = await dbContext.CompanionCognitiveProfiles.FirstOrDefaultAsync(x => x.CompanionId == request.CompanionId, cancellationToken)
                    ?? throw new InvalidOperationException($"No profile state exists for companion '{request.CompanionId:D}'.");
        var targetVersion = await ResolveVersionOrThrowAsync(request.CompanionId, request.ProfileVersionId, cancellationToken);
        if (targetVersion.ValidationStatus is "Rejected" or "Draft")
        {
            throw new InvalidOperationException($"Profile version '{targetVersion.ProfileVersionId:D}' is not activatable.");
        }

        var companion = await dbContext.Companions.FirstOrDefaultAsync(x => x.CompanionId == request.CompanionId, cancellationToken)
                        ?? throw new InvalidOperationException($"Companion '{request.CompanionId:D}' not found.");

        var previousActiveVersionId = state.ActiveProfileVersionId;
        var now = DateTimeOffset.UtcNow;
        state.ActiveProfileVersionId = request.ProfileVersionId;
        state.StagedProfileVersionId = null;
        state.UpdatedAtUtc = now;
        state.UpdatedByUserId = normalizedActor;
        companion.ActiveCognitiveProfileVersionId = request.ProfileVersionId;

        var previousVersion = await dbContext.CompanionCognitiveProfileVersions
            .FirstOrDefaultAsync(x => x.ProfileVersionId == previousActiveVersionId, cancellationToken);
        if (previousVersion is not null && previousVersion.ProfileVersionId != request.ProfileVersionId)
        {
            previousVersion.ValidationStatus = "Deprecated";
        }

        targetVersion.ValidationStatus = "Active";
        dbContext.CompanionCognitiveProfileAudits.Add(
            new CompanionCognitiveProfileAuditEntity
            {
                AuditId = Guid.NewGuid(),
                CompanionId = request.CompanionId,
                ActorUserId = normalizedActor,
                Action = "Activate",
                FromProfileVersionId = previousActiveVersionId,
                ToProfileVersionId = request.ProfileVersionId,
                DiffJson = "{}",
                Reason = request.Reason?.Trim(),
                CreatedAtUtc = now
            });

        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetStateAsync(request.CompanionId, cancellationToken);
    }

    public async Task<CompanionCognitiveProfileState> RollbackAsync(RollbackCompanionCognitiveProfileRequest request, CancellationToken cancellationToken = default)
    {
        var state = await ActivateAsync(
            new ActivateCompanionCognitiveProfileRequest(
                request.CompanionId,
                request.TargetProfileVersionId,
                request.ActorUserId,
                request.Reason ?? "Rollback"),
            cancellationToken);

        var now = DateTimeOffset.UtcNow;
        dbContext.CompanionCognitiveProfileAudits.Add(
            new CompanionCognitiveProfileAuditEntity
            {
                AuditId = Guid.NewGuid(),
                CompanionId = request.CompanionId,
                ActorUserId = NormalizeActor(request.ActorUserId),
                Action = "Rollback",
                FromProfileVersionId = null,
                ToProfileVersionId = request.TargetProfileVersionId,
                DiffJson = "{}",
                Reason = request.Reason?.Trim(),
                CreatedAtUtc = now
            });
        await dbContext.SaveChangesAsync(cancellationToken);
        return state;
    }

    public async Task<IReadOnlyList<CompanionCognitiveProfileAudit>> GetAuditsAsync(Guid companionId, int take = 100, CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.CompanionCognitiveProfileAudits
            .AsNoTracking()
            .Where(x => x.CompanionId == companionId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync(cancellationToken);

        return rows.Select(
                x => new CompanionCognitiveProfileAudit(
                    x.AuditId,
                    x.CompanionId,
                    x.ActorUserId,
                    x.Action,
                    x.FromProfileVersionId,
                    x.ToProfileVersionId,
                    x.DiffJson,
                    x.Reason,
                    x.CreatedAtUtc))
            .ToArray();
    }

    public async Task<IReadOnlyList<CompanionCognitiveRuntimeTrace>> GetRuntimeTracesAsync(Guid companionId, int take = 200, CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.CompanionCognitiveRuntimeTraces
            .AsNoTracking()
            .Where(x => x.CompanionId == companionId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync(cancellationToken);

        return rows.Select(
                x => new CompanionCognitiveRuntimeTrace(
                    x.TraceId,
                    x.CompanionId,
                    x.SessionId,
                    x.ProfileVersionId,
                    x.RequestCorrelationId,
                    x.Phase,
                    x.DecisionJson,
                    x.LatencyMs,
                    x.CreatedAtUtc))
            .ToArray();
    }

    public async Task<SimulateCompanionCognitiveProfileResult> SimulateAsync(SimulateCompanionCognitiveProfileRequest request, CancellationToken cancellationToken = default)
    {
        var validation = CompanionCognitiveProfileValidation.Validate(request.Profile);
        var normalized = validation.NormalizedProfile ?? CompanionCognitiveProfileValidation.CreateDefault();
        var policy = BuildRuntimePolicy(request.CompanionId, Guid.Empty, 0, normalized);
        var selectedLayers = InferSelectedLayers(request.Query, normalized);
        var retrievalWeighting = JsonSerializer.SerializeToElement(normalized.Memory.RetrievalWeights, JsonOptions);
        var limits = JsonSerializer.SerializeToElement(policy.Limits, JsonOptions);
        return new SimulateCompanionCognitiveProfileResult(validation, selectedLayers, retrievalWeighting, limits);
    }

    public async Task<ResolvedCompanionCognitiveProfile> ResolveByCompanionIdAsync(Guid companionId, CancellationToken cancellationToken = default)
    {
        var state = await dbContext.CompanionCognitiveProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CompanionId == companionId, cancellationToken);
        if (state is null)
        {
            logger.LogDebug("No cognitive profile state found for companion {CompanionId}. Falling back to defaults.", companionId);
            var fallback = CompanionCognitiveProfileValidation.CreateDefault();
            return new ResolvedCompanionCognitiveProfile(
                companionId,
                Guid.Empty,
                0,
                fallback,
                BuildRuntimePolicy(companionId, Guid.Empty, 0, fallback),
                IsFallback: true);
        }

        var version = await dbContext.CompanionCognitiveProfileVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ProfileVersionId == state.ActiveProfileVersionId, cancellationToken);
        if (version is null)
        {
            logger.LogWarning(
                "Active cognitive profile version {ProfileVersionId} is missing for companion {CompanionId}. Falling back to defaults.",
                state.ActiveProfileVersionId,
                companionId);
            var fallback = CompanionCognitiveProfileValidation.CreateDefault();
            return new ResolvedCompanionCognitiveProfile(
                companionId,
                Guid.Empty,
                0,
                fallback,
                BuildRuntimePolicy(companionId, Guid.Empty, 0, fallback),
                IsFallback: true);
        }

        var profile = DeserializeProfile(version.ProfileJson);
        CompanionCognitiveRuntimePolicy runtimePolicy;
        try
        {
            runtimePolicy = JsonSerializer.Deserialize<CompanionCognitiveRuntimePolicy>(version.CompiledRuntimeJson, JsonOptions)
                            ?? BuildRuntimePolicy(companionId, version.ProfileVersionId, version.VersionNumber, profile);
        }
        catch
        {
            runtimePolicy = BuildRuntimePolicy(companionId, version.ProfileVersionId, version.VersionNumber, profile);
        }

        return new ResolvedCompanionCognitiveProfile(
            companionId,
            version.ProfileVersionId,
            version.VersionNumber,
            profile,
            runtimePolicy,
            IsFallback: false);
    }

    public async Task<ResolvedCompanionCognitiveProfile> ResolveBySessionIdAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("sessionId is required.", nameof(sessionId));
        }

        var companionId = await companionScopeResolver.ResolveCompanionIdOrThrowAsync(sessionId.Trim(), cancellationToken);
        return await ResolveByCompanionIdAsync(companionId, cancellationToken);
    }

    public async Task WriteAsync(
        Guid companionId,
        string sessionId,
        Guid profileVersionId,
        string requestCorrelationId,
        string phase,
        string decisionJson,
        int latencyMs,
        CancellationToken cancellationToken = default)
    {
        var entity = new CompanionCognitiveRuntimeTraceEntity
        {
            TraceId = Guid.NewGuid(),
            CompanionId = companionId,
            SessionId = string.IsNullOrWhiteSpace(sessionId) ? string.Empty : sessionId.Trim(),
            ProfileVersionId = profileVersionId,
            RequestCorrelationId = string.IsNullOrWhiteSpace(requestCorrelationId) ? string.Empty : requestCorrelationId.Trim(),
            Phase = string.IsNullOrWhiteSpace(phase) ? "unknown" : phase.Trim().ToLowerInvariant(),
            DecisionJson = string.IsNullOrWhiteSpace(decisionJson) ? "{}" : decisionJson.Trim(),
            LatencyMs = Math.Max(0, latencyMs),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        dbContext.CompanionCognitiveRuntimeTraces.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string NormalizeActor(string actorUserId)
        => string.IsNullOrWhiteSpace(actorUserId) ? "system" : actorUserId.Trim();

    private async Task<CompanionCognitiveProfileVersionEntity> ResolveVersionOrThrowAsync(
        Guid companionId,
        Guid profileVersionId,
        CancellationToken cancellationToken)
    {
        return await dbContext.CompanionCognitiveProfileVersions
                   .FirstOrDefaultAsync(
                       x => x.CompanionId == companionId && x.ProfileVersionId == profileVersionId,
                       cancellationToken)
               ?? throw new InvalidOperationException($"Profile version '{profileVersionId:D}' does not belong to companion '{companionId:D}'.");
    }

    private static CompanionCognitiveProfileVersion ToVersion(CompanionCognitiveProfileVersionEntity entity)
        => new(
            entity.ProfileVersionId,
            entity.CompanionId,
            entity.VersionNumber,
            entity.SchemaVersion,
            entity.ValidationStatus,
            entity.ProfileHash,
            entity.CreatedByUserId,
            entity.ChangeSummary,
            entity.ChangeReason,
            entity.CreatedAtUtc,
            entity.ProfileJson,
            entity.CompiledRuntimeJson);

    private static CompanionCognitiveProfileDocument DeserializeProfile(string profileJson)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<CompanionCognitiveProfileDocument>(profileJson, JsonOptions);
            return CompanionCognitiveProfileValidation.Validate(parsed ?? CompanionCognitiveProfileValidation.CreateDefault()).NormalizedProfile
                   ?? CompanionCognitiveProfileValidation.CreateDefault();
        }
        catch
        {
            return CompanionCognitiveProfileValidation.CreateDefault();
        }
    }

    private static CompanionCognitiveRuntimePolicy BuildRuntimePolicy(
        Guid companionId,
        Guid profileVersionId,
        int versionNumber,
        CompanionCognitiveProfileDocument profile)
    {
        var validated = CompanionCognitiveProfileValidation.Validate(profile).NormalizedProfile
                        ?? CompanionCognitiveProfileValidation.CreateDefault();
        return new CompanionCognitiveRuntimePolicy(
            companionId,
            profileVersionId,
            versionNumber,
            validated,
            new RuntimeLimits(
                MaxRetrieveCandidates: Math.Clamp(validated.Memory.MaxCandidates, 10, 400),
                MaxRetrieveResults: Math.Clamp(validated.Memory.MaxResults, 1, 100),
                MaxDebateTurns: Math.Clamp(validated.Reflection.Debate.TurnCap, 3, 16),
                MaxSelfCritiquePasses: Math.Clamp(validated.Reflection.MaxSelfCritiquePasses, 0, 3)));
    }

    private static string[] InferSelectedLayers(string query, CompanionCognitiveProfileDocument profile)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return ["working", "semantic"];
        }

        var normalized = query.Trim().ToLowerInvariant();
        if (normalized.Contains("identity", StringComparison.Ordinal)
            || normalized.Contains("name", StringComparison.Ordinal)
            || normalized.Contains("origin", StringComparison.Ordinal)
            || normalized.Contains("birth", StringComparison.Ordinal))
        {
            return profile.Memory.LayerPriorities.IdentityBoost >= 0.5 ? ["self", "semantic"] : ["semantic", "self"];
        }

        return
        [
            "working",
            profile.Memory.LayerPriorities.Semantic >= profile.Memory.LayerPriorities.Episodic ? "semantic" : "episodic"
        ];
    }

    private static string ComputeHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }
}

internal static class CompanionCognitiveProfileValidation
{
    public static CompanionCognitiveProfileDocument CreateDefault()
        => new();

    public static CompanionCognitiveProfileValidationResult Validate(CompanionCognitiveProfileDocument? profile)
    {
        var normalized = profile is null ? CreateDefault() : Clone(profile);
        var errors = new List<string>();
        var warnings = new List<string>();

        normalized.SchemaVersion = string.IsNullOrWhiteSpace(normalized.SchemaVersion) ? "1.0.0" : normalized.SchemaVersion.Trim();
        normalized.Attention.FocusStickiness = Clamp01(normalized.Attention.FocusStickiness, 0.65);
        normalized.Attention.ExplorationBreadth = Math.Clamp(normalized.Attention.ExplorationBreadth, 1, 8);
        normalized.Attention.ClarificationFrequency = Clamp01(normalized.Attention.ClarificationFrequency, 0.2);
        NormalizeAllocation(normalized.Attention.ContextWindowAllocation);

        var weights = normalized.Memory.RetrievalWeights;
        weights.Recency = ClampNonNegative(weights.Recency, 0.8);
        weights.SemanticMatch = ClampNonNegative(weights.SemanticMatch, 1.0);
        weights.EvidenceStrength = ClampNonNegative(weights.EvidenceStrength, 0.7);
        weights.RelationshipDegree = ClampNonNegative(weights.RelationshipDegree, 0.45);
        weights.Confidence = ClampNonNegative(weights.Confidence, 0.65);
        NormalizeRetrievalWeights(weights);

        normalized.Memory.LayerPriorities.Working = Clamp01(normalized.Memory.LayerPriorities.Working, 0.2);
        normalized.Memory.LayerPriorities.Episodic = Clamp01(normalized.Memory.LayerPriorities.Episodic, 0.4);
        normalized.Memory.LayerPriorities.Semantic = Clamp01(normalized.Memory.LayerPriorities.Semantic, 0.6);
        normalized.Memory.LayerPriorities.Procedural = Clamp01(normalized.Memory.LayerPriorities.Procedural, 0.45);
        normalized.Memory.LayerPriorities.Self = Clamp01(normalized.Memory.LayerPriorities.Self, 0.5);
        normalized.Memory.LayerPriorities.IdentityBoost = Clamp01(normalized.Memory.LayerPriorities.IdentityBoost, 0.9);
        normalized.Memory.MaxCandidates = Math.Clamp(normalized.Memory.MaxCandidates, 10, 400);
        normalized.Memory.MaxResults = Math.Clamp(normalized.Memory.MaxResults, 1, 100);
        normalized.Memory.DedupeSensitivity = Clamp01(normalized.Memory.DedupeSensitivity, 0.6);
        normalized.Memory.WriteThresholds.ConfidenceMin = Clamp01(normalized.Memory.WriteThresholds.ConfidenceMin, 0.62);
        normalized.Memory.WriteThresholds.ImportanceMin = Clamp01(normalized.Memory.WriteThresholds.ImportanceMin, 0.55);
        normalized.Memory.Decay.SemanticDailyDecay = Clamp01(normalized.Memory.Decay.SemanticDailyDecay, 0.02);
        normalized.Memory.Decay.EpisodicDailyDecay = Clamp01(normalized.Memory.Decay.EpisodicDailyDecay, 0.04);
        normalized.Memory.Decay.ReinforcementMultiplier = Math.Clamp(normalized.Memory.Decay.ReinforcementMultiplier, 0.2, 3.0);

        normalized.Reasoning.ReasoningMode = NormalizeEnum(
            normalized.Reasoning.ReasoningMode,
            "hybrid",
            ["deductive", "abductive", "hybrid", "heuristic-first"]);
        normalized.Reasoning.StructureTemplate = NormalizeEnum(
            normalized.Reasoning.StructureTemplate,
            "evidence-first",
            ["terse", "outline-first", "evidence-first", "action-first"]);
        normalized.Reasoning.Depth = Math.Clamp(normalized.Reasoning.Depth, 1, 4);
        normalized.Reasoning.EvidenceStrictness = Clamp01(normalized.Reasoning.EvidenceStrictness, 0.7);

        normalized.Expression.VerbosityTarget = NormalizeEnum(normalized.Expression.VerbosityTarget, "balanced", ["concise", "balanced", "detailed"]);
        normalized.Expression.ToneStyle = string.IsNullOrWhiteSpace(normalized.Expression.ToneStyle) ? "professional" : normalized.Expression.ToneStyle.Trim().ToLowerInvariant();
        normalized.Expression.EmotionalExpressivity = Clamp01(normalized.Expression.EmotionalExpressivity, 0.2);
        normalized.Expression.FormatRigidity = Clamp01(normalized.Expression.FormatRigidity, 0.55);

        normalized.Reflection.SelfCritiqueRate = Clamp01(normalized.Reflection.SelfCritiqueRate, 0.25);
        normalized.Reflection.MaxSelfCritiquePasses = Math.Clamp(normalized.Reflection.MaxSelfCritiquePasses, 0, 3);
        normalized.Reflection.Debate.TriggerSensitivity = Clamp01(normalized.Reflection.Debate.TriggerSensitivity, 0.55);
        normalized.Reflection.Debate.TurnCap = Math.Clamp(normalized.Reflection.Debate.TurnCap, 3, 16);
        normalized.Reflection.Debate.TerminationConfidenceThreshold = Clamp01(normalized.Reflection.Debate.TerminationConfidenceThreshold, 0.78);
        normalized.Reflection.Debate.ConvergenceDeltaMin = Math.Clamp(normalized.Reflection.Debate.ConvergenceDeltaMin, 0.001, 0.25);

        normalized.Uncertainty.AnswerConfidenceThreshold = Clamp01(normalized.Uncertainty.AnswerConfidenceThreshold, 0.66);
        normalized.Uncertainty.ClarifyConfidenceThreshold = Clamp01(normalized.Uncertainty.ClarifyConfidenceThreshold, 0.5);
        normalized.Uncertainty.DeferConfidenceThreshold = Clamp01(normalized.Uncertainty.DeferConfidenceThreshold, 0.3);
        normalized.Uncertainty.ConflictEscalationThreshold = Clamp01(normalized.Uncertainty.ConflictEscalationThreshold, 0.74);
        if (!(normalized.Uncertainty.DeferConfidenceThreshold <= normalized.Uncertainty.ClarifyConfidenceThreshold
              && normalized.Uncertainty.ClarifyConfidenceThreshold <= normalized.Uncertainty.AnswerConfidenceThreshold))
        {
            errors.Add("Uncertainty thresholds must satisfy defer <= clarify <= answer.");
        }

        normalized.Adaptation.Procedurality = Clamp01(normalized.Adaptation.Procedurality, 0.58);
        normalized.Adaptation.Adaptivity = Clamp01(normalized.Adaptation.Adaptivity, 0.42);
        normalized.Adaptation.PolicyStrictness = Clamp01(normalized.Adaptation.PolicyStrictness, 0.65);

        normalized.Evolution.EvolutionMode = NormalizeEnum(normalized.Evolution.EvolutionMode, "propose-only", ["disabled", "propose-only", "supervised-auto"]);
        normalized.Evolution.MaxDailyDelta = Clamp01(normalized.Evolution.MaxDailyDelta, 0.06);
        normalized.Evolution.ApprovalPolicy = NormalizeEnum(normalized.Evolution.ApprovalPolicy, "human-required", ["human-required", "auto-low-risk"]);

        if (normalized.Reflection.SelfCritiqueEnabled
            && normalized.Reflection.SelfCritiqueRate > 0.8
            && normalized.Reflection.MaxSelfCritiquePasses > 2)
        {
            warnings.Add("Self-critique settings are high and may increase latency.");
        }

        if (normalized.Expression.EmotionalExpressivity > 0.85
            && normalized.Reasoning.EvidenceStrictness < 0.35
            && normalized.Uncertainty.DeferConfidenceThreshold < 0.2)
        {
            errors.Add("Unsafe profile: high emotional expressivity with low evidence strictness and low defer threshold.");
        }

        if (normalized.Adaptation.Adaptivity > 0.9
            && normalized.Adaptation.PolicyStrictness < 0.2
            && normalized.Evolution.EvolutionMode == "supervised-auto")
        {
            errors.Add("Unsafe profile: high adaptivity with low policy strictness and auto evolution.");
        }

        if (normalized.Memory.MaxCandidates < normalized.Memory.MaxResults)
        {
            errors.Add("memory.maxCandidates must be greater than or equal to memory.maxResults.");
        }

        return new CompanionCognitiveProfileValidationResult(errors.Count == 0, errors.ToArray(), warnings.ToArray(), normalized);
    }

    private static CompanionCognitiveProfileDocument Clone(CompanionCognitiveProfileDocument profile)
        => JsonSerializer.Deserialize<CompanionCognitiveProfileDocument>(JsonSerializer.Serialize(profile, new JsonSerializerOptions(JsonSerializerDefaults.Web)), new JsonSerializerOptions(JsonSerializerDefaults.Web))
           ?? CreateDefault();

    private static void NormalizeAllocation(ContextWindowAllocation allocation)
    {
        allocation.Working = ClampNonNegative(allocation.Working, 0.34);
        allocation.Episodic = ClampNonNegative(allocation.Episodic, 0.2);
        allocation.Semantic = ClampNonNegative(allocation.Semantic, 0.24);
        allocation.Procedural = ClampNonNegative(allocation.Procedural, 0.12);
        allocation.Self = ClampNonNegative(allocation.Self, 0.1);

        var total = allocation.Working + allocation.Episodic + allocation.Semantic + allocation.Procedural + allocation.Self;
        if (total <= 0)
        {
            allocation.Working = 0.34;
            allocation.Episodic = 0.2;
            allocation.Semantic = 0.24;
            allocation.Procedural = 0.12;
            allocation.Self = 0.1;
            return;
        }

        allocation.Working /= total;
        allocation.Episodic /= total;
        allocation.Semantic /= total;
        allocation.Procedural /= total;
        allocation.Self /= total;
    }

    private static void NormalizeRetrievalWeights(RetrievalWeighting weighting)
    {
        var total = weighting.Recency + weighting.SemanticMatch + weighting.EvidenceStrength + weighting.RelationshipDegree + weighting.Confidence;
        if (total <= 0)
        {
            weighting.Recency = 0.2;
            weighting.SemanticMatch = 0.26;
            weighting.EvidenceStrength = 0.19;
            weighting.RelationshipDegree = 0.13;
            weighting.Confidence = 0.22;
            return;
        }

        weighting.Recency /= total;
        weighting.SemanticMatch /= total;
        weighting.EvidenceStrength /= total;
        weighting.RelationshipDegree /= total;
        weighting.Confidence /= total;
    }

    private static string NormalizeEnum(string? value, string fallback, IReadOnlyCollection<string> allowed)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToLowerInvariant();
        return allowed.Contains(normalized) ? normalized : fallback;
    }

    private static double Clamp01(double value, double fallback)
        => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : fallback;

    private static double ClampNonNegative(double value, double fallback)
        => double.IsFinite(value) ? Math.Max(0, value) : fallback;
}
