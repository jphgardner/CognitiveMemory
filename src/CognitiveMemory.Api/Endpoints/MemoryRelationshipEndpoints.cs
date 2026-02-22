using CognitiveMemory.Application.Relationships;
using CognitiveMemory.Api.Security;
using CognitiveMemory.Domain.Memory;
using CognitiveMemory.Infrastructure.Persistence;

namespace CognitiveMemory.Api.Endpoints;

public static class MemoryRelationshipEndpoints
{
    public static IEndpointRouteBuilder MapMemoryRelationshipEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/relationships").WithTags("Relationships").RequireAuthorization();

        group.MapPost(
                "/",
                async (HttpContext httpContext, UpsertMemoryRelationshipDto request, IMemoryRelationshipService service, MemoryDbContext dbContext, CompanionOwnershipService ownershipService, CancellationToken cancellationToken) =>
                {
                    var companion = await ownershipService.ResolveOwnedCompanionAsync(httpContext.User, request.CompanionId, dbContext, cancellationToken);
                    if (companion is null || !string.Equals(companion.SessionId, request.SessionId, StringComparison.Ordinal))
                    {
                        return Results.NotFound();
                    }

                    var row = await service.UpsertAsync(
                        new UpsertMemoryRelationshipRequest(
                            request.SessionId,
                            request.FromType,
                            request.FromId,
                            request.ToType,
                            request.ToId,
                            request.RelationshipType,
                            request.Confidence,
                            request.Strength,
                            request.ValidFromUtc,
                            request.ValidToUtc,
                            request.MetadataJson),
                        cancellationToken);
                    return Results.Ok(ToDto(row));
                })
            .WithName("UpsertMemoryRelationship")
            .WithTags("Relationships");

        group.MapGet(
                "/by-session",
                async (
                    HttpContext httpContext,
                    Guid companionId,
                    string sessionId,
                    string? relationshipType,
                    MemoryRelationshipStatus? status,
                    int? take,
                    IMemoryRelationshipService service,
                    MemoryDbContext dbContext,
                    CompanionOwnershipService ownershipService,
                    CancellationToken cancellationToken) =>
                {
                    var companion = await ownershipService.ResolveOwnedCompanionAsync(httpContext.User, companionId, dbContext, cancellationToken);
                    if (companion is null || !string.Equals(companion.SessionId, sessionId, StringComparison.Ordinal))
                    {
                        return Results.NotFound();
                    }

                    var rows = await service.QueryBySessionAsync(sessionId, relationshipType, status, take ?? 200, cancellationToken);
                    return Results.Ok(rows.Select(ToDto));
                })
            .WithName("GetMemoryRelationshipsBySession")
            .WithTags("Relationships");

        group.MapGet(
                "/by-node",
                async (
                    HttpContext httpContext,
                    Guid companionId,
                    string sessionId,
                    MemoryNodeType nodeType,
                    string nodeId,
                    string? relationshipType,
                    int? take,
                    IMemoryRelationshipService service,
                    MemoryDbContext dbContext,
                    CompanionOwnershipService ownershipService,
                    CancellationToken cancellationToken) =>
                {
                    var companion = await ownershipService.ResolveOwnedCompanionAsync(httpContext.User, companionId, dbContext, cancellationToken);
                    if (companion is null || !string.Equals(companion.SessionId, sessionId, StringComparison.Ordinal))
                    {
                        return Results.NotFound();
                    }

                    var rows = await service.QueryByNodeAsync(sessionId, nodeType, nodeId, relationshipType, take ?? 200, cancellationToken);
                    return Results.Ok(rows.Select(ToDto));
                })
            .WithName("GetMemoryRelationshipsByNode")
            .WithTags("Relationships");

        group.MapPost(
                "/{relationshipId:guid}/retire",
                async (Guid relationshipId, IMemoryRelationshipService service, CancellationToken cancellationToken) =>
                {
                    var ok = await service.RetireAsync(relationshipId, cancellationToken);
                    return ok ? Results.Ok(new { relationshipId, status = "retired" }) : Results.NotFound();
                })
            .WithName("RetireMemoryRelationship")
            .WithTags("Relationships");

        group.MapPost(
                "/backfill/run-once",
                async (HttpContext httpContext, BackfillMemoryRelationshipsDto request, IMemoryRelationshipService service, MemoryDbContext dbContext, CompanionOwnershipService ownershipService, CancellationToken cancellationToken) =>
                {
                    if (!string.IsNullOrWhiteSpace(request.SessionId))
                    {
                        var companion = await ownershipService.ResolveOwnedCompanionBySessionAsync(httpContext.User, request.SessionId, dbContext, cancellationToken);
                        if (companion is null)
                        {
                            return Results.NotFound();
                        }
                    }

                    var result = await service.BackfillAsync(request.SessionId, request.Take ?? 2000, cancellationToken);
                    return Results.Ok(result);
                })
            .WithName("BackfillMemoryRelationships")
            .WithTags("Relationships");

        group.MapPost(
                "/extract/run-once",
                async (HttpContext httpContext, ExtractMemoryRelationshipsDto request, IMemoryRelationshipExtractionService service, MemoryDbContext dbContext, CompanionOwnershipService ownershipService, CancellationToken cancellationToken) =>
                {
                    if (string.IsNullOrWhiteSpace(request.SessionId))
                    {
                        return Results.BadRequest(new { error = "sessionId is required." });
                    }

                    var companion = await ownershipService.ResolveOwnedCompanionBySessionAsync(httpContext.User, request.SessionId, dbContext, cancellationToken);
                    if (companion is null)
                    {
                        return Results.NotFound();
                    }

                    var result = await service.ExtractAsync(
                        request.SessionId.Trim(),
                        request.Take ?? 200,
                        request.Apply ?? true,
                        cancellationToken);
                    return Results.Ok(result);
                })
            .WithName("ExtractMemoryRelationships")
            .WithTags("Relationships");

        return endpoints;
    }

    private static MemoryRelationshipDto ToDto(MemoryRelationship row)
        => new(
            row.RelationshipId,
            row.SessionId,
            row.FromType,
            row.FromId,
            row.ToType,
            row.ToId,
            row.RelationshipType,
            row.Confidence,
            row.Strength,
            row.Status,
            row.ValidFromUtc,
            row.ValidToUtc,
            row.MetadataJson,
            row.CreatedAtUtc,
            row.UpdatedAtUtc);
}

public sealed record UpsertMemoryRelationshipDto(
    Guid CompanionId,
    string SessionId,
    MemoryNodeType FromType,
    string FromId,
    MemoryNodeType ToType,
    string ToId,
    string RelationshipType,
    double Confidence = 0.7,
    double Strength = 0.7,
    DateTimeOffset? ValidFromUtc = null,
    DateTimeOffset? ValidToUtc = null,
    string? MetadataJson = null);

public sealed record BackfillMemoryRelationshipsDto(string? SessionId = null, int? Take = 2000);
public sealed record ExtractMemoryRelationshipsDto(string SessionId, int? Take = 200, bool? Apply = true);

public sealed record MemoryRelationshipDto(
    Guid RelationshipId,
    string SessionId,
    MemoryNodeType FromType,
    string FromId,
    MemoryNodeType ToType,
    string ToId,
    string RelationshipType,
    double Confidence,
    double Strength,
    MemoryRelationshipStatus Status,
    DateTimeOffset? ValidFromUtc,
    DateTimeOffset? ValidToUtc,
    string? MetadataJson,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
