using CognitiveMemory.Application.Abstractions;

namespace CognitiveMemory.Api.Endpoints;

public static class ToolInvocationAuditEndpoints
{
    public static IEndpointRouteBuilder MapToolInvocationAuditEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGet(
                "/api/tool-invocations",
                async (
                    DateTimeOffset? fromUtc,
                    DateTimeOffset? toUtc,
                    int? take,
                    IToolInvocationAuditRepository repository,
                    CancellationToken cancellationToken) =>
                {
                    var to = toUtc ?? DateTimeOffset.UtcNow;
                    var from = fromUtc ?? to.AddHours(-1);

                    var rows = await repository.QueryByExecutedAtRangeAsync(
                        from,
                        to,
                        take ?? 50,
                        cancellationToken);

                    return Results.Ok(rows);
                })
            .WithName("GetToolInvocations")
            .WithTags("ToolInvocations");

        return endpoints;
    }
}
