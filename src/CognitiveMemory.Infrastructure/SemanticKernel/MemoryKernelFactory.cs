using CognitiveMemory.Application.AI;
using OpenAI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Embeddings;
using System.ClientModel;

namespace CognitiveMemory.Infrastructure.SemanticKernel;

public sealed class MemoryKernelFactory(IOptions<SemanticKernelOptions> options) : IMemoryKernelFactory
{
    public Kernel CreateKernel()
    {
        var settings = options.Value;
        var builder = Kernel.CreateBuilder();

        if (string.Equals(settings.Provider, "Ollama", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(settings.OllamaEndpoint))
            {
                throw new InvalidOperationException("SemanticKernel Ollama provider requires SemanticKernel:OllamaEndpoint.");
            }

            var endpoint = new Uri(settings.OllamaEndpoint);
            builder.AddOllamaChatCompletion(
                modelId: settings.ChatModelId,
                endpoint: endpoint);
            builder.AddOllamaTextEmbeddingGeneration(
                modelId: settings.EmbeddingModelId,
                endpoint: endpoint);
            return builder.Build();
        }

        if (string.Equals(settings.Provider, "OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(settings.OpenAiApiKey))
            {
                throw new InvalidOperationException("SemanticKernel OpenAI provider requires SemanticKernel:OpenAiApiKey.");
            }

            if (!string.IsNullOrWhiteSpace(settings.OpenAiEndpoint))
            {
                var clientOptions = new OpenAIClientOptions
                {
                    Endpoint = new Uri(settings.OpenAiEndpoint)
                };
                var client = new OpenAIClient(new ApiKeyCredential(settings.OpenAiApiKey), clientOptions);
                builder.AddOpenAIChatCompletion(settings.ChatModelId, client);
                builder.AddOpenAITextEmbeddingGeneration(settings.EmbeddingModelId, client);
            }
            else
            {
                builder.AddOpenAIChatCompletion(
                    modelId: settings.ChatModelId,
                    apiKey: settings.OpenAiApiKey);
                builder.AddOpenAITextEmbeddingGeneration(
                    modelId: settings.EmbeddingModelId,
                    apiKey: settings.OpenAiApiKey);
            }
            return builder.Build();
        }

        builder.Services.AddSingleton<IChatCompletionService, InMemoryChatCompletionService>();
        builder.Services.AddSingleton<ITextEmbeddingGenerationService, InMemoryTextEmbeddingGenerationService>();

        return builder.Build();
    }
}
