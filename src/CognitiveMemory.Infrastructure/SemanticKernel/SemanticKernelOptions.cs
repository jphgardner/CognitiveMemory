namespace CognitiveMemory.Infrastructure.SemanticKernel;

public sealed class SemanticKernelOptions
{
    public string Provider { get; set; } = "Ollama";
    public string ChatModelId { get; set; } = string.Empty;
    public string? LoopModelId { get; set; }
    public string? ClaimExtractionModelId { get; set; }
    public string? ClaimExtractionProvider { get; set; }
    public string? OllamaEndpoint { get; set; }
    public string? ClaimExtractionOllamaEndpoint { get; set; }
    public string? EmbeddingModelId { get; set; }
    public string? EmbeddingProvider { get; set; }
    public string? EmbeddingOllamaEndpoint { get; set; }
    public bool EnableVectorSearch { get; set; } = true;
    public int LexicalCandidateMultiplier { get; set; } = 6;
    public int VectorCandidateMultiplier { get; set; } = 6;
    public int HybridRrfK { get; set; } = 60;
    public int MaxVectorCandidatePool { get; set; } = 3000;
    public int LazyEmbeddingBackfillTake { get; set; } = 24;
    public string? OpenAiApiKey { get; set; }
    public string? EmbeddingOpenAiApiKey { get; set; }
    public int ChatResponseTimeoutSeconds { get; set; } = 120;
}
