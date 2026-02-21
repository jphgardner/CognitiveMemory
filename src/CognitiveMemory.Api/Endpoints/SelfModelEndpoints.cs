using CognitiveMemory.Application.SelfModel;

namespace CognitiveMemory.Api.Endpoints;

public static class SelfModelEndpoints
{
    public static IEndpointRouteBuilder MapSelfModelEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/self-model/preferences",
                async (ISelfModelService service, CancellationToken cancellationToken) =>
                {
                    var snapshot = await service.GetAsync(cancellationToken);
                    return Results.Ok(snapshot);
                })
            .WithName("GetSelfModelPreferences")
            .WithTags("SelfModel");

        endpoints.MapPost(
                "/api/self-model/preferences",
                async (SetPreferenceDto request, ISelfModelService service, CancellationToken cancellationToken) =>
                {
                    await service.SetPreferenceAsync(request.Key, request.Value, cancellationToken);
                    return Results.NoContent();
                })
            .WithName("SetSelfModelPreference")
            .WithTags("SelfModel");

        return endpoints;
    }
}

public sealed record SetPreferenceDto(string Key, string Value);
