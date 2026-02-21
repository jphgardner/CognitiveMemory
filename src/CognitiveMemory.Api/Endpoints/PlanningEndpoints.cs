using CognitiveMemory.Application.Planning;

namespace CognitiveMemory.Api.Endpoints;

public static class PlanningEndpoints
{
    public static IEndpointRouteBuilder MapPlanningEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/planning/goals",
                async (GenerateGoalPlanDto request, IGoalPlanningService service, CancellationToken cancellationToken) =>
                {
                    var plan = await service.GeneratePlanAsync(
                        new GenerateGoalPlanRequest(request.SessionId, request.Goal, request.LookbackDays, request.MaxSteps),
                        cancellationToken);

                    return Results.Ok(plan);
                })
            .WithName("GenerateGoalPlan")
            .WithTags("Planning");

        endpoints.MapPost(
                "/api/planning/goals/{planId:guid}/outcome",
                async (
                    Guid planId,
                    RecordGoalOutcomeDto request,
                    IGoalPlanningService service,
                    CancellationToken cancellationToken) =>
                {
                    var result = await service.RecordOutcomeAsync(
                        new RecordGoalOutcomeRequest(
                            planId,
                            request.SessionId,
                            request.Goal,
                            request.Succeeded,
                            request.ExecutedSteps,
                            request.OutcomeSummary,
                            request.Trigger),
                        cancellationToken);

                    return Results.Ok(result);
                })
            .WithName("RecordGoalPlanOutcome")
            .WithTags("Planning");

        return endpoints;
    }
}

public sealed record GenerateGoalPlanDto(
    string SessionId,
    string Goal,
    int? LookbackDays = null,
    int MaxSteps = 8);

public sealed record RecordGoalOutcomeDto(
    string SessionId,
    string Goal,
    bool Succeeded,
    IReadOnlyList<string> ExecutedSteps,
    string OutcomeSummary,
    string? Trigger = null);
