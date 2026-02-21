using CognitiveMemory.Domain.Memory;

namespace CognitiveMemory.Application.Semantic;

public sealed record CreateSemanticClaimRequest(
    string Subject,
    string Predicate,
    string Value,
    double Confidence,
    string Scope,
    SemanticClaimStatus Status = SemanticClaimStatus.Active,
    DateTimeOffset? ValidFromUtc = null,
    DateTimeOffset? ValidToUtc = null);
