namespace CognitiveMemory.Application.Contracts;

public sealed class DebateResult
{
    public string Answer { get; init; } = string.Empty;

    public double Confidence { get; init; }

    public IReadOnlyList<AnswerCitation> Citations { get; init; } = [];

    public IReadOnlyList<string> UncertaintyFlags { get; init; } = [];

    public IReadOnlyList<QueryContradictionItem> Contradictions { get; init; } = [];
}
