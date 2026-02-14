namespace CognitiveMemory.Application.AI;

public sealed class ExtractedClaim
{
    public string? SubjectKey { get; init; }

    public string? SubjectName { get; init; }

    public string? SubjectType { get; init; }

    public required string Predicate { get; init; }

    public string? LiteralValue { get; init; }

    public double Confidence { get; init; }

    public required string EvidenceSummary { get; init; }
}
