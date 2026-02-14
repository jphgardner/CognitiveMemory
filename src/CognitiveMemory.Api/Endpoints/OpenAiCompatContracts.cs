using System.Text.Json;
using System.Text.Json.Serialization;
using CognitiveMemory.Application.Contracts;

namespace CognitiveMemory.Api.Endpoints;

public sealed class OpenAiChatCompletionRequest
{
    public string Model { get; set; } = "cognitivememory-chat";

    public List<OpenAiIncomingMessage> Messages { get; set; } = [];

    public string? User { get; set; }

    public bool Stream { get; set; }

    public double? Temperature { get; set; }

    public int? MaxTokens { get; set; }
}

public sealed class OpenAiIncomingMessage
{
    public string Role { get; set; } = "user";

    public string Content { get; set; } = string.Empty;
}

public sealed class OpenAiChatCompletionResponse
{
    public string Id { get; init; } = string.Empty;

    public string Object { get; init; } = "chat.completion";

    public long Created { get; init; }

    public string Model { get; init; } = string.Empty;

    public IReadOnlyList<OpenAiChatCompletionChoice> Choices { get; init; } = [];

    public OpenAiUsage Usage { get; init; } = new();
}

public sealed class OpenAiChatCompletionChoice
{
    public int Index { get; init; }

    public OpenAiChatMessage Message { get; init; } = new();

    public string FinishReason { get; init; } = "stop";
}

public sealed class OpenAiChatMessage
{
    public string Role { get; init; } = "assistant";

    public string Content { get; init; } = string.Empty;

    public OpenAiChatMetadata Metadata { get; init; } = new();
}

public sealed class OpenAiChatMetadata
{
    public IReadOnlyList<AnswerCitation> Citations { get; init; } = [];

    public double Confidence { get; init; }

    public IReadOnlyList<string> UncertaintyFlags { get; init; } = [];

    public IReadOnlyList<QueryContradictionItem> Contradictions { get; init; } = [];

    public IReadOnlyList<OpenAiToolExecution> ToolExecutions { get; init; } = [];

    public AnswerConscience Conscience { get; init; } = new();
}

public sealed class OpenAiToolExecution
{
    public string ToolName { get; init; } = string.Empty;

    public string Source { get; init; } = "agent";

    public bool Ok { get; init; }

    public string Code { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public string IdempotencyKey { get; init; } = string.Empty;

    public string TraceId { get; init; } = string.Empty;

    public JsonElement? Data { get; init; }

    public IReadOnlyList<Guid> EventIds { get; init; } = [];

    public int ResultCount { get; init; }
}

public sealed class OpenAiUsage
{
    public int PromptTokens { get; init; }

    public int CompletionTokens { get; init; }

    public int TotalTokens { get; init; }
}

public sealed class OpenAiChatCompletionChunk
{
    public string Id { get; init; } = string.Empty;

    public string Object { get; init; } = "chat.completion.chunk";

    public long Created { get; init; }

    public string Model { get; init; } = string.Empty;

    public IReadOnlyList<OpenAiChatCompletionChunkChoice> Choices { get; init; } = [];
}

public sealed class OpenAiChatCompletionChunkChoice
{
    public int Index { get; init; }

    public OpenAiChatDelta Delta { get; init; } = new();

    public string? FinishReason { get; init; }
}

public sealed class OpenAiChatDelta
{
    public string? Role { get; init; }

    public string? Content { get; init; }

    public OpenAiChatMetadata? Metadata { get; init; }
}

public sealed class OpenAiModelListResponse
{
    public string Object { get; init; } = "list";

    public IReadOnlyList<OpenAiModelItem> Data { get; init; } = [];
}

public sealed class OpenAiModelItem
{
    public string Id { get; init; } = string.Empty;

    public string Object { get; init; } = "model";

    public long Created { get; init; }

    [JsonPropertyName("owned_by")]
    public string OwnedBy { get; init; } = "cognitivememory";

    [JsonPropertyName("permission")]
    public IReadOnlyList<object> Permission { get; init; } = Array.Empty<object>();
}

public sealed class OpenAiErrorResponse
{
    public OpenAiError Error { get; init; } = new();
}

public sealed class OpenAiError
{
    public string Message { get; init; } = string.Empty;

    public string Type { get; init; } = "invalid_request_error";

    public string? Param { get; init; }

    public string? Code { get; init; }
}

public sealed class OpenAiEmbeddingsRequest
{
    public string Model { get; set; } = "cognitivememory-embeddings";

    public JsonElement Input { get; set; }

    public IReadOnlyList<string> GetInputs()
    {
        if (Input.ValueKind == JsonValueKind.String)
        {
            return [Input.GetString() ?? string.Empty];
        }

        if (Input.ValueKind == JsonValueKind.Array)
        {
            return Input.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString() ?? string.Empty)
                .ToList();
        }

        return [];
    }
}

public sealed class OpenAiEmbeddingsResponse
{
    public string Object { get; init; } = "list";

    public IReadOnlyList<OpenAiEmbeddingData> Data { get; init; } = [];

    public string Model { get; init; } = string.Empty;

    public OpenAiEmbeddingUsage Usage { get; init; } = new();
}

public sealed class OpenAiEmbeddingData
{
    public string Object { get; init; } = "embedding";

    public int Index { get; init; }

    public IReadOnlyList<float> Embedding { get; init; } = [];
}

public sealed class OpenAiEmbeddingUsage
{
    public int PromptTokens { get; init; }

    public int TotalTokens { get; init; }
}
