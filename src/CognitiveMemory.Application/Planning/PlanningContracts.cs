namespace CognitiveMemory.Application.Planning;

public sealed record GenerateGoalPlanRequest(
    string SessionId,
    string Goal,
    int? LookbackDays = null,
    int MaxSteps = 8);

public sealed record GoalPlanStep(int Order, string Description, string Source);

public sealed record GoalPlanResult(
    Guid PlanId,
    string SessionId,
    string Goal,
    IReadOnlyList<GoalPlanStep> Steps,
    IReadOnlyList<string> SupportingSignals,
    DateTimeOffset GeneratedAtUtc);

public sealed record RecordGoalOutcomeRequest(
    Guid PlanId,
    string SessionId,
    string Goal,
    bool Succeeded,
    IReadOnlyList<string> ExecutedSteps,
    string OutcomeSummary,
    string? Trigger = null);

public sealed record RecordGoalOutcomeResult(
    Guid PlanId,
    Guid? RoutineId,
    bool ProceduralMemoryUpdated,
    DateTimeOffset RecordedAtUtc);
