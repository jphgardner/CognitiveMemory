using System.Text.Json;
using CognitiveMemory.Infrastructure.Events;
using CognitiveMemory.Infrastructure.Persistence;
using CognitiveMemory.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace CognitiveMemory.Infrastructure.Reactive;

public sealed class UserProfileProjectionConsumer(MemoryDbContext dbContext) : IOutboxEventConsumer
{
    public string ConsumerName => nameof(UserProfileProjectionConsumer);

    public bool CanHandle(string eventType)
        => eventType is MemoryEventTypes.SelfPreferenceSet or MemoryEventTypes.SemanticClaimCreated;

    public async Task HandleAsync(OutboxEvent @event, CancellationToken cancellationToken = default)
    {
        if (string.Equals(@event.EventType, MemoryEventTypes.SelfPreferenceSet, StringComparison.Ordinal))
        {
            if (!TryGetString(@event.PayloadJson, "key", out var key)
                || !TryGetString(@event.PayloadJson, "value", out var value))
            {
                return;
            }

            if (!TryGetGuid(@event.PayloadJson, "companionId", out var selfCompanionId))
            {
                return;
            }

            await UpsertAsync(selfCompanionId, key, value, source: "self", confidence: 1.0, cancellationToken);
            return;
        }

        if (!TryGetString(@event.PayloadJson, "subject", out var subject)
            || !TryGetString(@event.PayloadJson, "predicate", out var predicate)
            || !TryGetString(@event.PayloadJson, "value", out var semanticValue))
        {
            return;
        }

        var keyFromClaim = InferProfileKey(subject, predicate);
        if (string.IsNullOrWhiteSpace(keyFromClaim))
        {
            return;
        }

        var confidence = TryGetDouble(@event.PayloadJson, "confidence", out var parsedConfidence)
            ? Math.Clamp(parsedConfidence, 0, 1)
            : 0.6;
        if (!TryGetGuid(@event.PayloadJson, "companionId", out var semanticCompanionId))
        {
            return;
        }

        await UpsertAsync(semanticCompanionId, keyFromClaim, semanticValue, source: "semantic", confidence, cancellationToken);
    }

    private async Task UpsertAsync(Guid companionId, string key, string value, string source, double confidence, CancellationToken cancellationToken)
    {
        var normalizedKey = NormalizeProfileKey(key);
        var existing = await dbContext.UserProfileProjections
            .FirstOrDefaultAsync(x => x.CompanionId == companionId && x.Key == normalizedKey, cancellationToken);
        if (existing is null)
        {
            dbContext.UserProfileProjections.Add(
                new UserProfileProjectionEntity
                {
                    CompanionId = companionId,
                    Key = normalizedKey,
                    Value = value,
                    Source = source,
                    Confidence = confidence,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                });
        }
        else
        {
            existing.Value = value;
            existing.Source = source;
            existing.Confidence = confidence;
            existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string? InferProfileKey(string subject, string predicate)
    {
        var s = subject.Trim().ToLowerInvariant();
        var p = predicate.Trim().ToLowerInvariant();

        if (s.StartsWith("identity.", StringComparison.Ordinal))
        {
            return s;
        }

        if (p.Contains("name", StringComparison.Ordinal) || s.Contains("name", StringComparison.Ordinal))
        {
            return "identity.name";
        }

        if (p.Contains("birth", StringComparison.Ordinal) || s.Contains("birth", StringComparison.Ordinal))
        {
            return "identity.birth_datetime";
        }

        if (p.Contains("role", StringComparison.Ordinal) || s.Contains("role", StringComparison.Ordinal))
        {
            return "identity.role";
        }

        if (p.Contains("origin", StringComparison.Ordinal) || s.Contains("origin", StringComparison.Ordinal))
        {
            return "identity.origin";
        }

        return null;
    }

    private static string NormalizeProfileKey(string key)
        => key.Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_').Replace('/', '_');

    private static bool TryGetString(string payloadJson, string key, out string value)
    {
        value = string.Empty;
        try
        {
            using var json = JsonDocument.Parse(payloadJson);
            if (!json.RootElement.TryGetProperty(key, out var token))
            {
                return false;
            }

            value = token.GetString() ?? string.Empty;
            return value.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetDouble(string payloadJson, string key, out double value)
    {
        value = 0;
        try
        {
            using var json = JsonDocument.Parse(payloadJson);
            if (!json.RootElement.TryGetProperty(key, out var token))
            {
                return false;
            }

            value = token.GetDouble();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetGuid(string payloadJson, string key, out Guid value)
    {
        value = Guid.Empty;
        try
        {
            using var json = JsonDocument.Parse(payloadJson);
            if (!json.RootElement.TryGetProperty(key, out var token))
            {
                return false;
            }

            if (token.ValueKind == JsonValueKind.String)
            {
                return Guid.TryParse(token.GetString(), out value);
            }

            value = token.GetGuid();
            return value != Guid.Empty;
        }
        catch
        {
            return false;
        }
    }
}
