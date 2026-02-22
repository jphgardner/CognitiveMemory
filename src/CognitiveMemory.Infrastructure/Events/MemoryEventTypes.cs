namespace CognitiveMemory.Infrastructure.Events;

public static class MemoryEventTypes
{
    public const string EpisodicMemoryCreated = "EpisodicMemoryCreated";
    public const string SemanticClaimCreated = "SemanticClaimCreated";
    public const string SemanticClaimSuperseded = "SemanticClaimSuperseded";
    public const string SemanticEvidenceAdded = "SemanticEvidenceAdded";
    public const string SemanticContradictionAdded = "SemanticContradictionAdded";
    public const string ProceduralRoutineUpserted = "ProceduralRoutineUpserted";
    public const string SelfPreferenceSet = "SelfPreferenceSet";
    public const string ToolInvocationCompleted = "ToolInvocationCompleted";
    public const string ScheduledActionCreated = "ScheduledActionCreated";
    public const string ScheduledActionExecuted = "ScheduledActionExecuted";
    public const string ScheduledActionRetrying = "ScheduledActionRetrying";
    public const string ScheduledActionFailed = "ScheduledActionFailed";
    public const string ScheduledActionCanceled = "ScheduledActionCanceled";
    public const string SubconsciousDebateRequested = "SubconsciousDebateRequested";
    public const string SubconsciousDebateStarted = "SubconsciousDebateStarted";
    public const string SubconsciousDebateTurnCompleted = "SubconsciousDebateTurnCompleted";
    public const string SubconsciousDebateAwaitingUserInput = "SubconsciousDebateAwaitingUserInput";
    public const string SubconsciousDebateConcluded = "SubconsciousDebateConcluded";
    public const string SubconsciousOutcomeValidationFailed = "SubconsciousOutcomeValidationFailed";
    public const string SubconsciousMemoryUpdateApplied = "SubconsciousMemoryUpdateApplied";
    public const string SubconsciousMemoryUpdateSkipped = "SubconsciousMemoryUpdateSkipped";
    public const string SubconsciousMemoryUpdateDeferred = "SubconsciousMemoryUpdateDeferred";
    public const string SubconsciousDebateFailed = "SubconsciousDebateFailed";
    public const string MemoryRelationshipCreated = "MemoryRelationshipCreated";
    public const string MemoryRelationshipUpdated = "MemoryRelationshipUpdated";
    public const string MemoryRelationshipRetired = "MemoryRelationshipRetired";
}
