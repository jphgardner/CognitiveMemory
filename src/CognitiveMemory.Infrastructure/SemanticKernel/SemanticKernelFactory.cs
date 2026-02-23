using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.Ollama;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace CognitiveMemory.Infrastructure.SemanticKernel;

public sealed class SemanticKernelFactory(SemanticKernelOptions options)
{
    public Kernel CreateChatKernel()
        => CreateForModel(options.ChatModelId, options.Provider, options.OllamaEndpoint);

    public Kernel CreateClaimExtractionKernel()
    {
        var modelId = FirstNonEmpty(options.ClaimExtractionModelId, options.LoopModelId, options.ChatModelId);
        var provider = FirstNonEmpty(options.ClaimExtractionProvider, options.Provider);
        var ollamaEndpoint = FirstNonEmpty(options.ClaimExtractionOllamaEndpoint, options.OllamaEndpoint, "http://localhost:11434");
        return CreateForModel(modelId, provider, ollamaEndpoint);
    }

    private Kernel CreateForModel(string modelId, string provider, string? ollamaEndpoint)
    {
        var kernelBuilder = Kernel.CreateBuilder();

        if (string.Equals(provider, "OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(options.OpenAiApiKey))
            {
                throw new InvalidOperationException("SemanticKernel.OpenAiApiKey or OPENAI_API_KEY must be configured for OpenAI provider.");
            }

            kernelBuilder.AddOpenAIChatCompletion(modelId, options.OpenAiApiKey);
            return kernelBuilder.Build();
        }

        if (string.Equals(provider, "Ollama", StringComparison.OrdinalIgnoreCase))
        {
            var endpoint = string.IsNullOrWhiteSpace(ollamaEndpoint) ? "http://localhost:11434" : ollamaEndpoint;
            kernelBuilder.AddOllamaChatCompletion(modelId, new Uri(endpoint));
            return kernelBuilder.Build();
        }

        throw new InvalidOperationException($"Unsupported SemanticKernel provider '{provider}'. Configure Provider=OpenAI or Provider=Ollama.");
    }

    private static string FirstNonEmpty(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate.Trim();
            }
        }

        throw new InvalidOperationException("No SemanticKernel model id was configured.");
    }
}
