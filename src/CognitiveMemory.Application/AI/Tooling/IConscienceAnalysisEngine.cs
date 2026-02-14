using CognitiveMemory.Application.Contracts;

namespace CognitiveMemory.Application.AI.Tooling;

public sealed class ConscienceAnalysisInput
{
    public Guid SourceEventId { get; init; }

    public string SourceEventType { get; init; } = string.Empty;

    public QueryCandidate Claim { get; init; } = new();
}

public class ConscienceAnalysisResult
{
    public string Decision { get; init; } = ConsciencePolicy.Approve;

    public double RiskScore { get; init; }

    public double RecommendedConfidence { get; init; }

    public IReadOnlyList<string> ReasonCodes { get; init; } = [];

    public string Summary { get; init; } = string.Empty;

    public IReadOnlyList<string> Keywords { get; init; } = [];

    public bool UsedFallback { get; init; }

    public string ModelId { get; init; } = string.Empty;
}

public interface IConscienceAnalysisEngine
{
    Task<ConscienceAnalysisResult> AnalyzeClaimAsync(ConscienceAnalysisInput input, CancellationToken cancellationToken);
}
