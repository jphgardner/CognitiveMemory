using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CognitiveMemory.Application.Chat;
using CognitiveMemory.Infrastructure.SemanticKernel;

namespace CognitiveMemory.Api.Endpoints;

public static class OpenAiCompatibleEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapOpenAiCompatibleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/v1").WithTags("OpenAI-Compatible");

        group.MapGet("/models", (HttpRequest request) =>
        {
            if (!HasBearerToken(request))
            {
                return Results.Unauthorized();
            }

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return Results.Ok(
                new
                {
                    @object = "list",
                    data = new object[]
                    {
                        new
                        {
                            id = "cognitivememory-chat",
                            @object = "model",
                            created = now,
                            owned_by = "cognitivememory"
                        },
                        new
                        {
                            id = "cognitivememory-embedding",
                            @object = "model",
                            created = now,
                            owned_by = "cognitivememory"
                        }
                    }
                });
        });

        group.MapGet("/models/{id}", (HttpRequest request, string id) =>
        {
            if (!HasBearerToken(request))
            {
                return Results.Unauthorized();
            }

            if (!string.Equals(id, "cognitivememory-chat", StringComparison.Ordinal)
                && !string.Equals(id, "cognitivememory-embedding", StringComparison.Ordinal))
            {
                return Results.NotFound(new { error = new { message = $"Model '{id}' not found.", type = "invalid_request_error" } });
            }

            return Results.Ok(
                new
                {
                    id,
                    @object = "model",
                    created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    owned_by = "cognitivememory"
                });
        });

        group.MapPost(
            "/chat/completions",
            async (HttpContext httpContext, OpenAiChatCompletionsRequest request, IChatService chatService, CancellationToken cancellationToken) =>
            {
                if (!HasBearerToken(httpContext.Request))
                {
                    return Results.Unauthorized();
                }

                var userMessage = ExtractLatestUserMessage(request.Messages);
                if (string.IsNullOrWhiteSpace(userMessage))
                {
                    return Results.BadRequest(new { error = new { message = "At least one user message is required.", type = "invalid_request_error" } });
                }

                var sessionId = string.IsNullOrWhiteSpace(request.User)
                    ? Guid.NewGuid().ToString("N")
                    : request.User.Trim();
                var model = string.IsNullOrWhiteSpace(request.Model) ? "cognitivememory-chat" : request.Model.Trim();

                if (request.Stream == true)
                {
                    await StreamCompletionAsync(httpContext, model, sessionId, userMessage.Trim(), chatService, cancellationToken);
                    return Results.Empty;
                }

                var response = await chatService.AskAsync(new ChatRequest(userMessage.Trim(), sessionId), cancellationToken);
                var completionId = $"chatcmpl_{Guid.NewGuid():N}";
                var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var payload = new
                {
                    id = completionId,
                    @object = "chat.completion",
                    created,
                    model,
                    choices = new[]
                    {
                        new
                        {
                            index = 0,
                            message = new
                            {
                                role = "assistant",
                                content = response.Answer
                            },
                            finish_reason = "stop"
                        }
                    },
                    usage = new
                    {
                        prompt_tokens = EstimateTokenCount(userMessage),
                        completion_tokens = EstimateTokenCount(response.Answer),
                        total_tokens = EstimateTokenCount(userMessage) + EstimateTokenCount(response.Answer)
                    }
                };

                return Results.Ok(payload);
            });

        group.MapPost(
            "/embeddings",
            async (HttpRequest request, OpenAiEmbeddingsRequest body, ITextEmbeddingGenerator embeddingGenerator, CancellationToken cancellationToken) =>
            {
                if (!HasBearerToken(request))
                {
                    return Results.Unauthorized();
                }

                var inputs = NormalizeEmbeddingInputs(body.Input);
                if (inputs.Count == 0)
                {
                    return Results.BadRequest(new { error = new { message = "input is required.", type = "invalid_request_error" } });
                }

                var data = new List<object>(inputs.Count);
                var totalPromptTokens = 0;
                for (var i = 0; i < inputs.Count; i++)
                {
                    var input = inputs[i];
                    var embedding = await embeddingGenerator.GenerateAsync(input, cancellationToken) ?? CreateDeterministicEmbedding(input);
                    data.Add(
                        new
                        {
                            @object = "embedding",
                            index = i,
                            embedding
                        });
                    totalPromptTokens += EstimateTokenCount(input);
                }

                return Results.Ok(
                    new
                    {
                        @object = "list",
                        data,
                        model = string.IsNullOrWhiteSpace(body.Model) ? "cognitivememory-embedding" : body.Model.Trim(),
                        usage = new
                        {
                            prompt_tokens = totalPromptTokens,
                            total_tokens = totalPromptTokens
                        }
                    });
            });

        return endpoints;
    }

    private static async Task StreamCompletionAsync(
        HttpContext httpContext,
        string model,
        string sessionId,
        string message,
        IChatService chatService,
        CancellationToken cancellationToken)
    {
        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        httpContext.Response.ContentType = "text/event-stream";
        httpContext.Response.Headers.CacheControl = "no-cache";
        httpContext.Response.Headers.Append("X-Accel-Buffering", "no");
        await httpContext.Response.StartAsync(cancellationToken);

        var completionId = $"chatcmpl_{Guid.NewGuid():N}";
        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        await foreach (var chunk in chatService.AskStreamAsync(new ChatRequest(message, sessionId), cancellationToken))
        {
            if (!string.IsNullOrEmpty(chunk.Delta))
            {
                var deltaPayload = new
                {
                    id = completionId,
                    @object = "chat.completion.chunk",
                    created,
                    model,
                    choices = new[]
                    {
                        new
                        {
                            index = 0,
                            delta = new { content = chunk.Delta },
                            finish_reason = (string?)null
                        }
                    }
                };
                await WriteChunkAsync(httpContext.Response, deltaPayload, cancellationToken);
            }

            if (chunk.IsFinal)
            {
                var finalPayload = new
                {
                    id = completionId,
                    @object = "chat.completion.chunk",
                    created,
                    model,
                    choices = new[]
                    {
                        new
                        {
                            index = 0,
                            delta = new { },
                            finish_reason = "stop"
                        }
                    }
                };
                await WriteChunkAsync(httpContext.Response, finalPayload, cancellationToken);
            }
        }

        await httpContext.Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
        await httpContext.Response.Body.FlushAsync(cancellationToken);
    }

    private static async Task WriteChunkAsync(HttpResponse response, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        await response.WriteAsync($"data: {json}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    private static bool HasBearerToken(HttpRequest request)
    {
        if (!request.Headers.TryGetValue("Authorization", out var value))
        {
            return false;
        }

        var header = value.ToString().Trim();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var token = header["Bearer ".Length..].Trim();
        return token.Length > 0;
    }

    private static int EstimateTokenCount(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        return Math.Max(1, text.Length / 4);
    }

    private static string? ExtractLatestUserMessage(IReadOnlyList<OpenAiChatMessage>? messages)
    {
        if (messages is null || messages.Count == 0)
        {
            return null;
        }

        for (var i = messages.Count - 1; i >= 0; i--)
        {
            var message = messages[i];
            if (!string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var content = message.Content?.Trim();
            if (!string.IsNullOrWhiteSpace(content))
            {
                return content;
            }
        }

        return messages[^1].Content?.Trim();
    }

    private static IReadOnlyList<string> NormalizeEmbeddingInputs(JsonElement input)
    {
        if (input.ValueKind == JsonValueKind.String)
        {
            var text = input.GetString();
            return string.IsNullOrWhiteSpace(text) ? [] : [text.Trim()];
        }

        if (input.ValueKind == JsonValueKind.Array)
        {
            var output = new List<string>();
            foreach (var item in input.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var text = item.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    output.Add(text.Trim());
                }
            }

            return output;
        }

        return [];
    }

    private static float[] CreateDeterministicEmbedding(string text)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text.Trim().ToLowerInvariant()));
        var dimensions = 32;
        var output = new float[dimensions];
        for (var i = 0; i < dimensions; i++)
        {
            output[i] = (hash[i] - 127.5f) / 127.5f;
        }

        return output;
    }
}

public sealed record OpenAiChatCompletionsRequest(
    string? Model,
    IReadOnlyList<OpenAiChatMessage>? Messages,
    bool? Stream,
    string? User);

public sealed record OpenAiChatMessage(string Role, string? Content);

public sealed record OpenAiEmbeddingsRequest(string? Model, JsonElement Input);
