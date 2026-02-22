using CognitiveMemory.Application.Semantic;
using CognitiveMemory.Api.Security;
using CognitiveMemory.Infrastructure.Persistence;
using CognitiveMemory.Domain.Memory;

namespace CognitiveMemory.Api.Endpoints;

public static class SemanticMemoryEndpoints
{
    public static IEndpointRouteBuilder MapSemanticMemoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/semantic").WithTags("Semantic").RequireAuthorization();

        group.MapPost(
                "/claims",
                async (HttpContext httpContext, CreateSemanticClaimDto request, ISemanticMemoryService service, MemoryDbContext dbContext, CompanionOwnershipService ownershipService, CancellationToken cancellationToken) =>
                {
                    var companion = await ownershipService.ResolveOwnedCompanionAsync(httpContext.User, request.CompanionId, dbContext, cancellationToken);
                    if (companion is null)
                    {
                        return Results.NotFound();
                    }

                    var created = await service.CreateClaimAsync(
                        new CreateSemanticClaimRequest(
                            string.IsNullOrWhiteSpace(request.Subject) ? $"session:{companion.SessionId}" : request.Subject,
                            request.Predicate,
                            request.Value,
                            request.Confidence,
                            request.Scope,
                            request.Status,
                            request.ValidFromUtc,
                            request.ValidToUtc),
                        cancellationToken);

                    return Results.Ok(ToClaimDto(created));
                })
            .WithName("CreateSemanticClaim")
            .WithTags("Semantic");

        group.MapGet(
                "/claims",
                async (
                    HttpContext httpContext,
                    Guid companionId,
                    string? subject,
                    string? predicate,
                    SemanticClaimStatus? status,
                    int? take,
                    ISemanticMemoryService service,
                    MemoryDbContext dbContext,
                    CompanionOwnershipService ownershipService,
                    CancellationToken cancellationToken) =>
                {
                    var companion = await ownershipService.ResolveOwnedCompanionAsync(httpContext.User, companionId, dbContext, cancellationToken);
                    if (companion is null)
                    {
                        return Results.NotFound();
                    }

                    var scopedSubject = $"session:{companion.SessionId}";
                    if (!string.IsNullOrWhiteSpace(subject)
                        && !subject.Contains(companion.SessionId, StringComparison.Ordinal))
                    {
                        return Results.BadRequest(new { error = "subject is outside companion scope." });
                    }

                    var claims = await service.QueryClaimsAsync(subject ?? scopedSubject, predicate, status, take ?? 100, cancellationToken);
                    return Results.Ok(claims.Select(ToClaimDto));
                })
            .WithName("QuerySemanticClaims")
            .WithTags("Semantic");

        group.MapPost(
                "/claims/{claimId:guid}/evidence",
                async (Guid claimId, AddClaimEvidenceDto request, ISemanticMemoryService service, CancellationToken cancellationToken) =>
                {
                    var evidence = await service.AddEvidenceAsync(
                        new AddClaimEvidenceRequest(
                            claimId,
                            request.SourceType,
                            request.SourceReference,
                            request.ExcerptOrSummary,
                            request.Strength,
                            request.CapturedAtUtc),
                        cancellationToken);

                    return Results.Ok(ToEvidenceDto(evidence));
                })
            .WithName("AddClaimEvidence")
            .WithTags("Semantic");

        group.MapPost(
                "/contradictions",
                async (AddClaimContradictionDto request, ISemanticMemoryService service, CancellationToken cancellationToken) =>
                {
                    var contradiction = await service.AddContradictionAsync(
                        new AddClaimContradictionRequest(
                            request.ClaimAId,
                            request.ClaimBId,
                            request.Type,
                            request.Severity,
                            request.Status,
                            request.DetectedAtUtc),
                        cancellationToken);

                    return Results.Ok(ToContradictionDto(contradiction));
                })
            .WithName("AddClaimContradiction")
            .WithTags("Semantic");

        group.MapPost(
                "/claims/{claimId:guid}/supersede",
                async (Guid claimId, SupersedeClaimDto request, ISemanticMemoryService service, CancellationToken cancellationToken) =>
                {
                    var created = await service.SupersedeClaimAsync(
                        new SupersedeSemanticClaimRequest(
                            claimId,
                            request.Subject,
                            request.Predicate,
                            request.Value,
                            request.Confidence,
                            request.Scope),
                        cancellationToken);

                    return Results.Ok(ToClaimDto(created));
                })
            .WithName("SupersedeSemanticClaim")
            .WithTags("Semantic");

        group.MapPost(
                "/decay/run-once",
                async (DecayClaimsDto request, ISemanticMemoryService service, CancellationToken cancellationToken) =>
                {
                    var affected = await service.RunDecayAsync(
                        request.StaleDays,
                        request.DecayStep,
                        request.MinConfidence,
                        cancellationToken);
                    return Results.Ok(new { affected });
                })
            .WithName("RunSemanticDecayOnce")
            .WithTags("Semantic");

        return endpoints;
    }

    private static SemanticClaimDto ToClaimDto(CognitiveMemory.Domain.Memory.SemanticClaim claim) =>
        new(
            claim.ClaimId,
            claim.Subject,
            claim.Predicate,
            claim.Value,
            claim.Confidence,
            claim.Scope,
            claim.Status,
            claim.ValidFromUtc,
            claim.ValidToUtc,
            claim.SupersededByClaimId,
            claim.CreatedAtUtc,
            claim.UpdatedAtUtc);

    private static ClaimEvidenceDto ToEvidenceDto(CognitiveMemory.Domain.Memory.ClaimEvidence evidence) =>
        new(
            evidence.EvidenceId,
            evidence.ClaimId,
            evidence.SourceType,
            evidence.SourceReference,
            evidence.ExcerptOrSummary,
            evidence.Strength,
            evidence.CapturedAtUtc);

    private static ClaimContradictionDto ToContradictionDto(CognitiveMemory.Domain.Memory.ClaimContradiction contradiction) =>
        new(
            contradiction.ContradictionId,
            contradiction.ClaimAId,
            contradiction.ClaimBId,
            contradiction.Type,
            contradiction.Severity,
            contradiction.DetectedAtUtc,
            contradiction.Status);
}

public sealed record CreateSemanticClaimDto(
    Guid CompanionId,
    string Subject,
    string Predicate,
    string Value,
    double Confidence,
    string Scope = "global",
    SemanticClaimStatus Status = SemanticClaimStatus.Active,
    DateTimeOffset? ValidFromUtc = null,
    DateTimeOffset? ValidToUtc = null);

public sealed record SemanticClaimDto(
    Guid ClaimId,
    string Subject,
    string Predicate,
    string Value,
    double Confidence,
    string Scope,
    SemanticClaimStatus Status,
    DateTimeOffset? ValidFromUtc,
    DateTimeOffset? ValidToUtc,
    Guid? SupersededByClaimId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record AddClaimEvidenceDto(
    string SourceType,
    string SourceReference,
    string ExcerptOrSummary,
    double Strength,
    DateTimeOffset? CapturedAtUtc = null);

public sealed record ClaimEvidenceDto(
    Guid EvidenceId,
    Guid ClaimId,
    string SourceType,
    string SourceReference,
    string ExcerptOrSummary,
    double Strength,
    DateTimeOffset CapturedAtUtc);

public sealed record AddClaimContradictionDto(
    Guid ClaimAId,
    Guid ClaimBId,
    string Type,
    string Severity,
    string Status = "Open",
    DateTimeOffset? DetectedAtUtc = null);

public sealed record ClaimContradictionDto(
    Guid ContradictionId,
    Guid ClaimAId,
    Guid ClaimBId,
    string Type,
    string Severity,
    DateTimeOffset DetectedAtUtc,
    string Status);

public sealed record SupersedeClaimDto(
    string Subject,
    string Predicate,
    string Value,
    double Confidence,
    string Scope = "global");

public sealed record DecayClaimsDto(
    int StaleDays = 30,
    double DecayStep = 0.05,
    double MinConfidence = 0.2);
