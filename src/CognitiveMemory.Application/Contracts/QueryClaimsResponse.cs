namespace CognitiveMemory.Application.Contracts;

public sealed class QueryClaimsResponse
{
    public IReadOnlyList<QueryClaimItem> Claims { get; init; } = [];

    public QueryMeta Meta { get; init; } = new();
}

public sealed class QueryClaimItem
{
    public Guid ClaimId { get; init; }

    public string Predicate { get; init; } = string.Empty;

    public string? LiteralValue { get; init; }

    public double Score { get; init; }

    public double Confidence { get; init; }

    public IReadOnlyList<QueryEvidenceItem> Evidence { get; init; } = [];

    public IReadOnlyList<QueryContradictionItem> Contradictions { get; init; } = [];
}

public sealed class QueryEvidenceItem
{
    public Guid EvidenceId { get; init; }

    public string SourceType { get; init; } = string.Empty;

    public string SourceRef { get; init; } = string.Empty;

    public double Strength { get; init; }
}

public sealed class QueryContradictionItem
{
    public Guid ContradictionId { get; init; }

    public string Type { get; init; } = string.Empty;

    public string Severity { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;
}

public sealed class QueryMeta
{
    public string Strategy { get; init; } = "hybrid";

    public long LatencyMs { get; init; }

    public string RequestId { get; init; } = string.Empty;

    public IReadOnlyList<string> UncertaintyFlags { get; init; } = [];
}
