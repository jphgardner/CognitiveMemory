using CognitiveMemory.Application.AI.Tooling;

namespace CognitiveMemory.Application.Contracts;

public sealed class AnswerQuestionResponse
{
    public string Answer { get; init; } = string.Empty;

    public double Confidence { get; init; }

    public IReadOnlyList<AnswerCitation> Citations { get; init; } = [];

    public IReadOnlyList<string> UncertaintyFlags { get; init; } = [];

    public IReadOnlyList<QueryContradictionItem> Contradictions { get; init; } = [];

    public AnswerConscience Conscience { get; init; } = new();

    public string RequestId { get; init; } = string.Empty;
}

public sealed class AnswerCitation
{
    public Guid ClaimId { get; init; }

    public Guid EvidenceId { get; init; }
}

public sealed class AnswerConscience
{
    public string Decision { get; init; } = ConsciencePolicy.Approve;

    public double RiskScore { get; init; }

    public string PolicyVersion { get; init; } = ConsciencePolicy.CurrentVersion;

    public IReadOnlyList<string> ReasonCodes { get; init; } = [];
}
