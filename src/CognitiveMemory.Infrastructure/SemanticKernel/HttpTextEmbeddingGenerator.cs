using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace CognitiveMemory.Infrastructure.SemanticKernel;

public sealed class HttpTextEmbeddingGenerator(
    HttpClient httpClient,
    SemanticKernelOptions options,
    ILogger<HttpTextEmbeddingGenerator> logger) : ITextEmbeddingGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Uri OpenAiEmbeddingsPath = new("/v1/embeddings", UriKind.Relative);
    private static readonly Uri OllamaEmbeddingsPath = new("/api/embeddings", UriKind.Relative);
    private static readonly Uri OllamaEmbedPath = new("/api/embed", UriKind.Relative);

    public async Task<float[]?> GenerateAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var modelId = options.EmbeddingModelId;
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return null;
        }

        var provider = FirstNonEmpty(options.EmbeddingProvider, options.Provider);
        try
        {
            if (string.Equals(provider, "OpenAI", StringComparison.OrdinalIgnoreCase))
            {
                return await GenerateOpenAiEmbeddingAsync(modelId, text, cancellationToken);
            }

            if (string.Equals(provider, "Ollama", StringComparison.OrdinalIgnoreCase))
            {
                return await GenerateOllamaEmbeddingAsync(modelId, text, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Embedding generation failed. Provider={Provider} Model={ModelId}", provider, modelId);
            return null;
        }

        logger.LogWarning("Embedding provider not recognized. Provider={Provider}", provider);
        return null;
    }

    private async Task<float[]?> GenerateOpenAiEmbeddingAsync(string modelId, string text, CancellationToken cancellationToken)
    {
        var apiKey = FirstNonEmpty(options.EmbeddingOpenAiApiKey, options.OpenAiApiKey);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning("OpenAI embedding requested but no API key configured.");
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, OpenAiEmbeddingsPath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = JsonContent(
            new
            {
                model = modelId,
                input = text
            });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("OpenAI embedding request failed with status {StatusCode}", response.StatusCode);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!json.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0)
        {
            return null;
        }

        var embeddingElement = data[0].TryGetProperty("embedding", out var emb) ? emb : default;
        return ParseEmbeddingArray(embeddingElement);
    }

    private async Task<float[]?> GenerateOllamaEmbeddingAsync(string modelId, string text, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, OllamaEmbeddingsPath);
        request.Content = JsonContent(
            new
            {
                model = modelId,
                prompt = text
            });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // Newer Ollama versions use /api/embed.
            using var fallbackRequest = new HttpRequestMessage(HttpMethod.Post, OllamaEmbedPath);
            fallbackRequest.Content = JsonContent(
                new
                {
                    model = modelId,
                    input = text
                });

            using var fallbackResponse = await httpClient.SendAsync(fallbackRequest, cancellationToken);
            if (!fallbackResponse.IsSuccessStatusCode)
            {
                logger.LogWarning("Ollama embedding request failed with status {StatusCode}", fallbackResponse.StatusCode);
                return null;
            }

            return await ParseOllamaEmbeddingResponseAsync(fallbackResponse, cancellationToken);
        }

        return await ParseOllamaEmbeddingResponseAsync(response, cancellationToken);
    }

    private static async Task<float[]?> ParseOllamaEmbeddingResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (json.RootElement.TryGetProperty("embedding", out var embeddingElement))
        {
            return ParseEmbeddingArray(embeddingElement);
        }

        if (json.RootElement.TryGetProperty("embeddings", out var embeddingsElement)
            && embeddingsElement.ValueKind == JsonValueKind.Array
            && embeddingsElement.GetArrayLength() > 0)
        {
            return ParseEmbeddingArray(embeddingsElement[0]);
        }

        return null;
    }

    private static float[]? ParseEmbeddingArray(JsonElement embeddingElement)
    {
        if (embeddingElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var values = new float[embeddingElement.GetArrayLength()];
        var i = 0;
        foreach (var value in embeddingElement.EnumerateArray())
        {
            if (!value.TryGetSingle(out var parsed))
            {
                if (value.TryGetDouble(out var parsedDouble))
                {
                    parsed = (float)parsedDouble;
                }
                else
                {
                    return null;
                }
            }

            values[i++] = parsed;
        }

        return values;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static StringContent JsonContent(object payload)
        => new(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
}
