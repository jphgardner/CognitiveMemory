namespace CognitiveMemory.Application.Planning;

public interface IGoalPlanningService
{
    Task<GoalPlanResult> GeneratePlanAsync(GenerateGoalPlanRequest request, CancellationToken cancellationToken = default);
    Task<RecordGoalOutcomeResult> RecordOutcomeAsync(RecordGoalOutcomeRequest request, CancellationToken cancellationToken = default);
}
