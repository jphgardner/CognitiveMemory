using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CognitiveMemory.Application.AI.Tooling;

public sealed class ToolEnvelope
{
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    [JsonPropertyName("code")]
    public string Code { get; init; } = "ok";

    [JsonPropertyName("message")]
    public string Message { get; init; } = "Success";

    [JsonPropertyName("data")]
    public object? Data { get; init; }

    [JsonPropertyName("idempotencyKey")]
    public string IdempotencyKey { get; init; } = string.Empty;

    [JsonPropertyName("eventIds")]
    public IReadOnlyList<Guid> EventIds { get; init; } = [];

    [JsonPropertyName("traceId")]
    public string TraceId { get; init; } = string.Empty;
}

public static class ToolEnvelopeJson
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Success(
        object? data,
        string code,
        string message,
        string? idempotencyKey = null,
        IReadOnlyList<Guid>? eventIds = null,
        string? traceId = null)
    {
        return Serialize(new ToolEnvelope
        {
            Ok = true,
            Code = code,
            Message = message,
            Data = data,
            IdempotencyKey = idempotencyKey ?? string.Empty,
            EventIds = eventIds ?? [],
            TraceId = ResolveTraceId(traceId)
        });
    }

    public static string Failure(
        string code,
        string message,
        object? data = null,
        string? idempotencyKey = null,
        IReadOnlyList<Guid>? eventIds = null,
        string? traceId = null)
    {
        return Serialize(new ToolEnvelope
        {
            Ok = false,
            Code = code,
            Message = message,
            Data = data,
            IdempotencyKey = idempotencyKey ?? string.Empty,
            EventIds = eventIds ?? [],
            TraceId = ResolveTraceId(traceId)
        });
    }

    public static bool TryReadDataString(string? json, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            value = data.GetString() ?? string.Empty;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string ResolveTraceId(string? overrideTraceId = null)
    {
        if (!string.IsNullOrWhiteSpace(overrideTraceId))
        {
            return overrideTraceId;
        }

        return Activity.Current?.TraceId.ToString() ?? Activity.Current?.Id ?? Guid.NewGuid().ToString("N");
    }

    private static string Serialize(ToolEnvelope envelope) => JsonSerializer.Serialize(envelope, JsonOptions);
}
