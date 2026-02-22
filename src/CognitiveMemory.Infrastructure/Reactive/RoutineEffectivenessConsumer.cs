using System.Text.Json;
using CognitiveMemory.Infrastructure.Events;
using CognitiveMemory.Infrastructure.Persistence;
using CognitiveMemory.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CognitiveMemory.Infrastructure.Reactive;

public sealed class RoutineEffectivenessConsumer(
    MemoryDbContext dbContext,
    ILogger<RoutineEffectivenessConsumer> logger) : IOutboxEventConsumer
{
    public string ConsumerName => nameof(RoutineEffectivenessConsumer);

    public bool CanHandle(string eventType)
        => eventType is MemoryEventTypes.ProceduralRoutineUpserted or MemoryEventTypes.EpisodicMemoryCreated;

    public async Task HandleAsync(OutboxEvent @event, CancellationToken cancellationToken = default)
    {
        if (string.Equals(@event.EventType, MemoryEventTypes.ProceduralRoutineUpserted, StringComparison.Ordinal))
        {
            if (!TryGetGuid(@event.PayloadJson, "routineId", out var upsertRoutineId)
                || !TryGetString(@event.PayloadJson, "trigger", out var trigger))
            {
                return;
            }
            if (!TryGetGuid(@event.PayloadJson, "companionId", out var upsertCompanionId))
            {
                return;
            }

            var existing = await dbContext.ProceduralRoutineMetrics
                .FirstOrDefaultAsync(x => x.RoutineId == upsertRoutineId && x.CompanionId == upsertCompanionId, cancellationToken);
            if (existing is null)
            {
                dbContext.ProceduralRoutineMetrics.Add(
                    new ProceduralRoutineMetricEntity
                    {
                        RoutineId = upsertRoutineId,
                        CompanionId = upsertCompanionId,
                        Trigger = trigger,
                        SuccessCount = 0,
                        FailureCount = 0,
                        UpdatedAtUtc = DateTimeOffset.UtcNow
                    });
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return;
        }

        if (!TryGetString(@event.PayloadJson, "sourceReference", out var sourceReference)
            || !string.Equals(sourceReference, "api:planning:outcome", StringComparison.OrdinalIgnoreCase)
            || !TryGetString(@event.PayloadJson, "context", out var contextJson))
        {
            return;
        }

        if (!TryGetBoolFromJson(contextJson, "succeeded", out var succeeded))
        {
            return;
        }
        if (!TryGetGuid(@event.PayloadJson, "companionId", out var companionId))
        {
            return;
        }

        if (!TryGetGuidFromJson(contextJson, "routineId", out var routineId) || routineId == Guid.Empty)
        {
            return;
        }

        var metric = await dbContext.ProceduralRoutineMetrics
            .FirstOrDefaultAsync(x => x.RoutineId == routineId && x.CompanionId == companionId, cancellationToken);
        if (metric is null)
        {
            return;
        }

        if (succeeded)
        {
            metric.SuccessCount += 1;
        }
        else
        {
            metric.FailureCount += 1;
        }

        if (TryGetStringFromJson(contextJson, "outcome", out var outcome))
        {
            metric.LastOutcomeSummary = outcome;
        }

        metric.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Routine effectiveness updated. RoutineId={RoutineId} Success={Success} Failure={Failure}",
            routineId,
            metric.SuccessCount,
            metric.FailureCount);
    }

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

    private static bool TryGetBoolFromJson(string jsonString, string key, out bool value)
    {
        value = false;
        try
        {
            using var json = JsonDocument.Parse(jsonString);
            if (!json.RootElement.TryGetProperty(key, out var token))
            {
                return false;
            }

            value = token.GetBoolean();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetGuidFromJson(string jsonString, string key, out Guid value)
    {
        value = Guid.Empty;
        try
        {
            using var json = JsonDocument.Parse(jsonString);
            if (!json.RootElement.TryGetProperty(key, out var token))
            {
                return false;
            }

            if (token.ValueKind == JsonValueKind.Null)
            {
                return false;
            }

            if (token.ValueKind == JsonValueKind.String)
            {
                return Guid.TryParse(token.GetString(), out value);
            }

            value = token.GetGuid();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetStringFromJson(string jsonString, string key, out string value)
    {
        value = string.Empty;
        try
        {
            using var json = JsonDocument.Parse(jsonString);
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
}
