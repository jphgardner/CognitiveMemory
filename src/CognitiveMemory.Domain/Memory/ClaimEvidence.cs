namespace CognitiveMemory.Domain.Memory;

public sealed record ClaimEvidence(
    Guid EvidenceId,
    Guid ClaimId,
    string SourceType,
    string SourceReference,
    string ExcerptOrSummary,
    double Strength,
    DateTimeOffset CapturedAtUtc);
