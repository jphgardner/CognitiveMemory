namespace CognitiveMemory.Application.AI.Plugins;

public static class AgentPermissionModes
{
    public const string ReadOnly = "ReadOnly";
    public const string ReadWriteSafe = "ReadWriteSafe";
    public const string Privileged = "Privileged";

    public static readonly string[] All =
    [
        ReadOnly,
        ReadWriteSafe,
        Privileged
    ];
}

public sealed class AgentToolingOptions
{
    public const string SectionName = "AgentTooling";

    // Legacy compatibility switch. Prefer Mode.
    public bool? EnableWrites { get; init; }

    public string Mode { get; init; } = AgentPermissionModes.ReadOnly;

    public int MaxToolCallsPerRequest { get; init; } = 8;

    public int ToolTimeoutSeconds { get; init; } = 8;
}
