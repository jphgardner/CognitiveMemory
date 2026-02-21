using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.Ollama;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace CognitiveMemory.Infrastructure.SemanticKernel;

public sealed class SemanticKernelFactory(SemanticKernelOptions options)
{
    public Kernel CreateChatKernel() => CreateForModel(options.ChatModelId);

    public Kernel CreateClaimExtractionKernel()
    {
        var modelId = FirstNonEmpty(options.ClaimExtractionModelId, options.LoopModelId, options.ChatModelId);
        return CreateForModel(modelId);
    }

    private Kernel CreateForModel(string modelId)
    {
        var kernelBuilder = Kernel.CreateBuilder();

        if (string.Equals(options.Provider, "OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(options.OpenAiApiKey))
            {
                throw new InvalidOperationException("SemanticKernel.OpenAiApiKey or OPENAI_API_KEY must be configured for OpenAI provider.");
            }

            kernelBuilder.AddOpenAIChatCompletion(modelId, options.OpenAiApiKey);
            return kernelBuilder.Build();
        }

        if (string.Equals(options.Provider, "Ollama", StringComparison.OrdinalIgnoreCase))
        {
            var endpoint = options.OllamaEndpoint ?? "http://localhost:11434";
            kernelBuilder.AddOllamaChatCompletion(modelId, new Uri(endpoint));
            return kernelBuilder.Build();
        }

        // InMemory and unknown providers do not require an SK chat connector.
        return kernelBuilder.Build();
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
