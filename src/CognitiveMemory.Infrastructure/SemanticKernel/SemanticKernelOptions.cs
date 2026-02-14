namespace CognitiveMemory.Infrastructure.SemanticKernel;

public sealed class SemanticKernelOptions
{
    public const string SectionName = "SemanticKernel";

    public string Provider { get; init; } = "InMemory";

    public string? OpenAiApiKey { get; init; }

    public string? OpenAiEndpoint { get; init; }

    public string OllamaEndpoint { get; init; } = "http://localhost:11434";

    public string ChatModelId { get; init; } = "gpt-4o-mini";

    public string EmbeddingModelId { get; init; } = "text-embedding-3-small";

    public int EmbeddingTimeoutSeconds { get; init; } = 20;

    public int ClaimExtractionTimeoutSeconds { get; init; } = 20;

    public int ConscienceAnalysisTimeoutSeconds { get; init; } = 25;
}
