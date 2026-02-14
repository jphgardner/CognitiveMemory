namespace CognitiveMemory.Application.AI.Tooling;

public static class ConsciencePolicy
{
    public const string CurrentVersion = "policy-2026-02-13";

    public const string Approve = "Approve";
    public const string Downgrade = "Downgrade";
    public const string Revise = "Revise";
    public const string Block = "Block";

    public static IReadOnlyList<string> DecisionSet { get; } =
    [
        Approve,
        Downgrade,
        Revise,
        Block
    ];
}
