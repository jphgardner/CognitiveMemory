namespace CognitiveMemory.Application.AI.Tooling;

public sealed class PolicyDecisionWriteRequest
{
    public string SourceType { get; init; } = string.Empty;

    public string SourceRef { get; init; } = string.Empty;

    public string Decision { get; init; } = string.Empty;

    public double RiskScore { get; init; }

    public string PolicyVersion { get; init; } = string.Empty;

    public IReadOnlyList<string> ReasonCodes { get; init; } = [];

    public string MetadataJson { get; init; } = "{}";
}

public sealed class PolicyDecisionRecord
{
    public Guid DecisionId { get; init; }

    public string SourceType { get; init; } = string.Empty;

    public string SourceRef { get; init; } = string.Empty;

    public string Decision { get; init; } = string.Empty;

    public double RiskScore { get; init; }

    public string PolicyVersion { get; init; } = string.Empty;

    public IReadOnlyList<string> ReasonCodes { get; init; } = [];

    public string MetadataJson { get; init; } = "{}";

    public DateTimeOffset CreatedAt { get; init; }
}

public interface IPolicyDecisionRepository
{
    Task<Guid> SaveAsync(PolicyDecisionWriteRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<PolicyDecisionRecord>> GetBySourceAsync(string sourceType, string sourceRef, CancellationToken cancellationToken);

    Task<IReadOnlyList<PolicyDecisionRecord>> GetRecentAsync(int take, CancellationToken cancellationToken);
}
