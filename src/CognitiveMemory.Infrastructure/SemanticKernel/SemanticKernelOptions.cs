namespace CognitiveMemory.Infrastructure.SemanticKernel;

public sealed class SemanticKernelOptions
{
    public string Provider { get; set; } = "Ollama";
    public string ChatModelId { get; set; } = string.Empty;
    public string? LoopModelId { get; set; }
    public string? ClaimExtractionModelId { get; set; }
    public string? OllamaEndpoint { get; set; }
    public string? OpenAiApiKey { get; set; }
    public int ChatResponseTimeoutSeconds { get; set; } = 120;
}
