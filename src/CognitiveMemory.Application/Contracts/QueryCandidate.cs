namespace CognitiveMemory.Application.Contracts;

public sealed class QueryCandidate
{
    public Guid ClaimId { get; init; }

    public string Predicate { get; init; } = string.Empty;

    public string? LiteralValue { get; init; }

    public string Scope { get; init; } = "{}";

    public double Confidence { get; init; }

    public DateTimeOffset? LastReinforcedAt { get; init; }

    public DateTimeOffset? ValidFrom { get; init; }

    public DateTimeOffset? ValidTo { get; init; }

    public IReadOnlyList<QueryEvidenceItem> Evidence { get; init; } = [];

    public IReadOnlyList<QueryContradictionItem> Contradictions { get; init; } = [];
}
