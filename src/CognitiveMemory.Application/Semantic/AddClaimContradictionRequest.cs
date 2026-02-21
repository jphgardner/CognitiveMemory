namespace CognitiveMemory.Application.Semantic;

public sealed record AddClaimContradictionRequest(
    Guid ClaimAId,
    Guid ClaimBId,
    string Type,
    string Severity,
    string Status = "Open",
    DateTimeOffset? DetectedAtUtc = null);
