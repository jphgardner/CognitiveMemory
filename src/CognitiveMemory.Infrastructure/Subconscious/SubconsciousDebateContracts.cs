using System.Text.Json;
using System.Text.Json.Serialization;

namespace CognitiveMemory.Infrastructure.Subconscious;

public sealed record SubconsciousDebateTopic(
    string TopicKey,
    string TriggerEventType,
    Guid? TriggerEventId,
    string TriggerPayloadJson);

public sealed record SubconsciousDebateRequest(
    Guid DebateId,
    string SessionId,
    string TopicKey,
    string TriggerEventType,
    Guid? TriggerEventId,
    string TriggerPayloadJson,
    DateTimeOffset CreatedAtUtc);

public sealed record SubconsciousDebateClaimCreate(
    string Subject,
    string Predicate,
    string Value,
    double Confidence,
    string Scope);

public sealed record SubconsciousDebateClaimSupersede(
    Guid ClaimId,
    SubconsciousDebateClaimCreate Replacement);

public sealed record SubconsciousDebateContradiction(
    Guid ClaimAId,
    Guid ClaimBId,
    string Severity,
    string Status);

public sealed record SubconsciousDebateProceduralUpdate(
    Guid? RoutineId,
    string Trigger,
    string Name,
    IReadOnlyList<string> Steps,
    string Outcome);

public sealed record SubconsciousDebateSelfUpdate(
    string Key,
    string Value,
    double Confidence,
    bool RequiresConfirmation);

public sealed record SubconsciousDebateEvidenceRef(
    string Source,
    string ReferenceId,
    double Weight);

public sealed record SubconsciousDebateOutcome(
    string DecisionType,
    double FinalConfidence,
    string ReasoningSummary,
    IReadOnlyList<SubconsciousDebateEvidenceRef> EvidenceRefs,
    IReadOnlyList<SubconsciousDebateClaimCreate> ClaimsToCreate,
    IReadOnlyList<SubconsciousDebateClaimSupersede> ClaimsToSupersede,
    IReadOnlyList<SubconsciousDebateContradiction> Contradictions,
    IReadOnlyList<SubconsciousDebateProceduralUpdate> ProceduralUpdates,
    IReadOnlyList<SubconsciousDebateSelfUpdate> SelfUpdates,
    bool RequiresUserInput,
    string? UserQuestion)
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
}

public sealed record SubconsciousValidationResult(
    bool IsValid,
    bool RequiresUserInput,
    string Status,
    string? Error,
    SubconsciousDebateOutcome? Outcome);

public sealed record SubconsciousApplySkip(
    string Category,
    string Reference,
    string Reason,
    double? Confidence);

public sealed record SubconsciousApplyReport(
    int ProposedClaimsToCreate,
    int AppliedClaimsToCreate,
    int ProposedClaimsToSupersede,
    int AppliedClaimsToSupersede,
    int ProposedProceduralUpdates,
    int AppliedProceduralUpdates,
    int ProposedSelfUpdates,
    int AppliedSelfUpdates,
    IReadOnlyList<SubconsciousApplySkip> Skipped,
    bool AnyApplied);

public enum SubconsciousSessionState
{
    Queued = 0,
    Running = 1,
    AwaitingUser = 2,
    Completed = 3,
    Failed = 4,
    Canceled = 5
}
