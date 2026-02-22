using System.Security.Claims;
using CognitiveMemory.Api.Security;
using CognitiveMemory.Application.Cognitive;
using CognitiveMemory.Infrastructure.Persistence;

namespace CognitiveMemory.Api.Endpoints;

public static class CompanionCognitiveProfileEndpoints
{
    public static IEndpointRouteBuilder MapCompanionCognitiveProfileEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/companions/{companionId:guid}/cognitive-profile")
            .WithTags("CompanionCognitiveProfile")
            .RequireAuthorization();

        group.MapGet(
                "/",
                async (
                    HttpContext httpContext,
                    Guid companionId,
                    ICompanionCognitiveProfileService service,
                    MemoryDbContext dbContext,
                    CompanionOwnershipService ownershipService,
                    CancellationToken cancellationToken) =>
                {
                    var companion = await ownershipService.ResolveOwnedCompanionAsync(httpContext.User, companionId, dbContext, cancellationToken);
                    if (companion is null)
                    {
                        return Results.NotFound();
                    }

                    CompanionCognitiveProfileState state;
                    try
                    {
                        state = await service.GetStateAsync(companionId, cancellationToken);
                    }
                    catch (InvalidOperationException)
                    {
                        var actorUserId = ResolveUserId(httpContext.User);
                        _ = await service.CreateVersionAsync(
                            new CreateCompanionCognitiveProfileVersionRequest(
                                companionId,
                                actorUserId,
                                new CompanionCognitiveProfileDocument(),
                                ChangeSummary: "Auto-bootstrap default profile",
                                ChangeReason: "Profile state was missing at read time"),
                            cancellationToken);
                        state = await service.GetStateAsync(companionId, cancellationToken);
                    }

                    var versions = await service.GetVersionsAsync(companionId, 1, cancellationToken);
                    return Results.Ok(new { state, active = versions.FirstOrDefault() });
                })
            .WithName("GetCompanionCognitiveProfile");

        group.MapGet(
                "/versions",
                async (
                    HttpContext httpContext,
                    Guid companionId,
                    int? take,
                    ICompanionCognitiveProfileService service,
                    MemoryDbContext dbContext,
                    CompanionOwnershipService ownershipService,
                    CancellationToken cancellationToken) =>
                {
                    var companion = await ownershipService.ResolveOwnedCompanionAsync(httpContext.User, companionId, dbContext, cancellationToken);
                    if (companion is null)
                    {
                        return Results.NotFound();
                    }

                    var versions = await service.GetVersionsAsync(companionId, take ?? 50, cancellationToken);
                    return Results.Ok(versions);
                })
            .WithName("ListCompanionCognitiveProfileVersions");

        group.MapPost(
                "/validate",
                async (
                    HttpContext httpContext,
                    Guid companionId,
                    ValidateCompanionCognitiveProfileRequest request,
                    ICompanionCognitiveProfileService service,
                    MemoryDbContext dbContext,
                    CompanionOwnershipService ownershipService,
                    CancellationToken cancellationToken) =>
                {
                    var companion = await ownershipService.ResolveOwnedCompanionAsync(httpContext.User, companionId, dbContext, cancellationToken);
                    if (companion is null)
                    {
                        return Results.NotFound();
                    }

                    var result = await service.ValidateAsync(request.Profile, cancellationToken);
                    return Results.Ok(result);
                })
            .WithName("ValidateCompanionCognitiveProfile");

        group.MapPost(
                "/versions",
                async (
                    HttpContext httpContext,
                    Guid companionId,
                    CreateCompanionCognitiveProfileVersionRequestDto request,
                    ICompanionCognitiveProfileService service,
                    MemoryDbContext dbContext,
                    CompanionOwnershipService ownershipService,
                    CancellationToken cancellationToken) =>
                {
                    var companion = await ownershipService.ResolveOwnedCompanionAsync(httpContext.User, companionId, dbContext, cancellationToken);
                    if (companion is null)
                    {
                        return Results.NotFound();
                    }

                    var actorUserId = ResolveUserId(httpContext.User);
                    var created = await service.CreateVersionAsync(
                        new CreateCompanionCognitiveProfileVersionRequest(
                            companionId,
                            actorUserId,
                            request.Profile,
                            request.ChangeSummary,
                            request.ChangeReason,
                            request.ValidateOnly),
                        cancellationToken);

                    return Results.Ok(created);
                })
            .WithName("CreateCompanionCognitiveProfileVersion");

        group.MapPost(
                "/stage",
                async (
                    HttpContext httpContext,
                    Guid companionId,
                    StageCompanionCognitiveProfileRequestDto request,
                    ICompanionCognitiveProfileService service,
                    MemoryDbContext dbContext,
                    CompanionOwnershipService ownershipService,
                    CancellationToken cancellationToken) =>
                {
                    var companion = await ownershipService.ResolveOwnedCompanionAsync(httpContext.User, companionId, dbContext, cancellationToken);
                    if (companion is null)
                    {
                        return Results.NotFound();
                    }

                    var state = await service.StageAsync(
                        new StageCompanionCognitiveProfileRequest(
                            companionId,
                            request.ProfileVersionId,
                            ResolveUserId(httpContext.User),
                            request.Reason),
                        cancellationToken);
                    return Results.Ok(state);
                })
            .WithName("StageCompanionCognitiveProfile");

        group.MapPost(
                "/activate",
                async (
                    HttpContext httpContext,
                    Guid companionId,
                    ActivateCompanionCognitiveProfileRequestDto request,
                    ICompanionCognitiveProfileService service,
                    MemoryDbContext dbContext,
                    CompanionOwnershipService ownershipService,
                    CancellationToken cancellationToken) =>
                {
                    var companion = await ownershipService.ResolveOwnedCompanionAsync(httpContext.User, companionId, dbContext, cancellationToken);
                    if (companion is null)
                    {
                        return Results.NotFound();
                    }

                    var state = await service.ActivateAsync(
                        new ActivateCompanionCognitiveProfileRequest(
                            companionId,
                            request.ProfileVersionId,
                            ResolveUserId(httpContext.User),
                            request.Reason),
                        cancellationToken);
                    return Results.Ok(state);
                })
            .WithName("ActivateCompanionCognitiveProfile");

        group.MapPost(
                "/rollback",
                async (
                    HttpContext httpContext,
                    Guid companionId,
                    RollbackCompanionCognitiveProfileRequestDto request,
                    ICompanionCognitiveProfileService service,
                    MemoryDbContext dbContext,
                    CompanionOwnershipService ownershipService,
                    CancellationToken cancellationToken) =>
                {
                    var companion = await ownershipService.ResolveOwnedCompanionAsync(httpContext.User, companionId, dbContext, cancellationToken);
                    if (companion is null)
                    {
                        return Results.NotFound();
                    }

                    var state = await service.RollbackAsync(
                        new RollbackCompanionCognitiveProfileRequest(
                            companionId,
                            request.TargetProfileVersionId,
                            ResolveUserId(httpContext.User),
                            request.Reason),
                        cancellationToken);
                    return Results.Ok(state);
                })
            .WithName("RollbackCompanionCognitiveProfile");

        group.MapGet(
                "/audit",
                async (
                    HttpContext httpContext,
                    Guid companionId,
                    int? take,
                    ICompanionCognitiveProfileService service,
                    MemoryDbContext dbContext,
                    CompanionOwnershipService ownershipService,
                    CancellationToken cancellationToken) =>
                {
                    var companion = await ownershipService.ResolveOwnedCompanionAsync(httpContext.User, companionId, dbContext, cancellationToken);
                    if (companion is null)
                    {
                        return Results.NotFound();
                    }

                    var rows = await service.GetAuditsAsync(companionId, take ?? 120, cancellationToken);
                    return Results.Ok(rows);
                })
            .WithName("ListCompanionCognitiveProfileAudits");

        group.MapGet(
                "/runtime-traces",
                async (
                    HttpContext httpContext,
                    Guid companionId,
                    int? take,
                    ICompanionCognitiveProfileService service,
                    MemoryDbContext dbContext,
                    CompanionOwnershipService ownershipService,
                    CancellationToken cancellationToken) =>
                {
                    var companion = await ownershipService.ResolveOwnedCompanionAsync(httpContext.User, companionId, dbContext, cancellationToken);
                    if (companion is null)
                    {
                        return Results.NotFound();
                    }

                    var rows = await service.GetRuntimeTracesAsync(companionId, take ?? 150, cancellationToken);
                    return Results.Ok(rows);
                })
            .WithName("ListCompanionCognitiveRuntimeTraces");

        group.MapPost(
                "/simulate",
                async (
                    HttpContext httpContext,
                    Guid companionId,
                    SimulateCompanionCognitiveProfileRequestDto request,
                    ICompanionCognitiveProfileService service,
                    MemoryDbContext dbContext,
                    CompanionOwnershipService ownershipService,
                    CancellationToken cancellationToken) =>
                {
                    var companion = await ownershipService.ResolveOwnedCompanionAsync(httpContext.User, companionId, dbContext, cancellationToken);
                    if (companion is null)
                    {
                        return Results.NotFound();
                    }

                    var result = await service.SimulateAsync(
                        new SimulateCompanionCognitiveProfileRequest(
                            companionId,
                            companion.SessionId,
                            request.Profile,
                            request.Query),
                        cancellationToken);
                    return Results.Ok(result);
                })
            .WithName("SimulateCompanionCognitiveProfile");

        return endpoints;
    }

    private static string ResolveUserId(ClaimsPrincipal user)
    {
        return user.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? user.FindFirstValue(ClaimTypes.Name)
               ?? "unknown";
    }
}

public sealed record ValidateCompanionCognitiveProfileRequest(CompanionCognitiveProfileDocument Profile);

public sealed record CreateCompanionCognitiveProfileVersionRequestDto(
    CompanionCognitiveProfileDocument Profile,
    string? ChangeSummary,
    string? ChangeReason,
    bool ValidateOnly = false);

public sealed record StageCompanionCognitiveProfileRequestDto(Guid ProfileVersionId, string? Reason);

public sealed record ActivateCompanionCognitiveProfileRequestDto(Guid ProfileVersionId, string? Reason);

public sealed record RollbackCompanionCognitiveProfileRequestDto(Guid TargetProfileVersionId, string? Reason);

public sealed record SimulateCompanionCognitiveProfileRequestDto(CompanionCognitiveProfileDocument Profile, string Query);
