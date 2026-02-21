namespace CognitiveMemory.Domain.Memory;

public sealed record ClaimContradiction(
    Guid ContradictionId,
    Guid ClaimAId,
    Guid ClaimBId,
    string Type,
    string Severity,
    DateTimeOffset DetectedAtUtc,
    string Status);
