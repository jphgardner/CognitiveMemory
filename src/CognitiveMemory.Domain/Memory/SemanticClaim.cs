namespace CognitiveMemory.Domain.Memory;

public sealed record SemanticClaim(
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
