using CognitiveMemory.Application.AI.Tooling;

namespace CognitiveMemory.Api.Endpoints;

public static class ConscienceEndpoints
{
    public static IEndpointRouteBuilder MapConscienceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/conscience");

        group.MapGet("/decisions/recent", async (int? take, IPolicyDecisionRepository repository, CancellationToken cancellationToken) =>
        {
            var decisions = await repository.GetRecentAsync(take ?? 50, cancellationToken);
            return Results.Ok(new
            {
                count = decisions.Count,
                decisions
            });
        });

        group.MapGet("/decisions/{sourceType}/{sourceRef}", async (string sourceType, string sourceRef, IPolicyDecisionRepository repository, CancellationToken cancellationToken) =>
        {
            var decisions = await repository.GetBySourceAsync(sourceType, sourceRef, cancellationToken);
            return Results.Ok(new
            {
                sourceType,
                sourceRef,
                count = decisions.Count,
                decisions
            });
        });

        group.MapGet("/replay/{requestId}", async (string requestId, IPolicyDecisionRepository repository, CancellationToken cancellationToken) =>
        {
            var decisions = await repository.GetBySourceAsync(PolicyDecisionSources.ChatAnswer, requestId, cancellationToken);
            var latest = decisions.FirstOrDefault();
            if (latest is null)
            {
                return Results.NotFound(new
                {
                    requestId,
                    message = "No persisted policy decision for this requestId."
                });
            }

            return Results.Ok(new
            {
                requestId,
                replay = new
                {
                    latest.Decision,
                    latest.RiskScore,
                    latest.PolicyVersion,
                    latest.ReasonCodes,
                    latest.MetadataJson,
                    latest.CreatedAt
                },
                historyCount = decisions.Count
            });
        });

        group.MapGet("/policy", () =>
        {
            return Results.Ok(new
            {
                policyVersion = ConsciencePolicy.CurrentVersion,
                decisions = ConsciencePolicy.DecisionSet,
                reasonCodes = ConscienceReasonCodes.Descriptions
            });
        });

        return app;
    }
}
