namespace CognitiveMemory.Application.Semantic;

public sealed record SupersedeSemanticClaimRequest(
    Guid ClaimId,
    string Subject,
    string Predicate,
    string Value,
    double Confidence,
    string Scope = "global");
