namespace CognitiveMemory.Application.Semantic;

public sealed record AddClaimEvidenceRequest(
    Guid ClaimId,
    string SourceType,
    string SourceReference,
    string ExcerptOrSummary,
    double Strength,
    DateTimeOffset? CapturedAtUtc = null);
