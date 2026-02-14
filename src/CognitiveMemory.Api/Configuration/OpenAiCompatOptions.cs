namespace CognitiveMemory.Api.Configuration;

public sealed class OpenAiCompatOptions
{
    public const string SectionName = "OpenAiCompat";

    public bool Enabled { get; init; } = true;

    public bool RequireApiKey { get; init; } = false;

    public bool AllowAnyNonEmptyApiKey { get; init; } = true;

    public List<string> AllowedApiKeys { get; init; } = [];

    public string DefaultModel { get; init; } = "cognitivememory-chat";

    public List<string> Models { get; init; } =
    [
        "cognitivememory-chat",
        "cognitivememory-fast",
        "cognitivememory-answer"
    ];

    public bool ExposeEmbeddings { get; init; } = true;
}
