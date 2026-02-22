using System.Text.Json;
using CognitiveMemory.Infrastructure.Events;
using CognitiveMemory.Infrastructure.Subconscious;
using Microsoft.Extensions.Logging;

namespace CognitiveMemory.Infrastructure.Reactive;

public sealed class SubconsciousDebateSchedulerConsumer(
    ISubconsciousDebateService debateService,
    SubconsciousDebateOptions options,
    ILogger<SubconsciousDebateSchedulerConsumer> logger) : IOutboxEventConsumer
{
    public string ConsumerName => nameof(SubconsciousDebateSchedulerConsumer);

    public bool CanHandle(string eventType)
        => eventType is MemoryEventTypes.EpisodicMemoryCreated
            or MemoryEventTypes.SemanticClaimCreated
            or MemoryEventTypes.SemanticContradictionAdded
            or MemoryEventTypes.SemanticEvidenceAdded
            or MemoryEventTypes.SemanticClaimSuperseded
            or MemoryEventTypes.ProceduralRoutineUpserted
            or MemoryEventTypes.SelfPreferenceSet;

    public async Task HandleAsync(OutboxEvent @event, CancellationToken cancellationToken = default)
    {
        if (!options.Enabled)
        {
            return;
        }

        if (!TryGetSessionId(@event.PayloadJson, out var sessionId))
        {
            return;
        }

        var topic = new SubconsciousDebateTopic(
            TopicKey: MapTopicKey(@event.EventType),
            TriggerEventType: @event.EventType,
            TriggerEventId: @event.EventId,
            TriggerPayloadJson: @event.PayloadJson);

        await debateService.QueueDebateAsync(sessionId, topic, cancellationToken);
        logger.LogDebug(
            "Subconscious debate queued from event. EventType={EventType} SessionId={SessionId}",
            @event.EventType,
            sessionId);
    }

    private static string MapTopicKey(string eventType)
        => eventType switch
        {
            MemoryEventTypes.SemanticContradictionAdded => "conflict-resolution",
            MemoryEventTypes.SelfPreferenceSet => "identity-evolution",
            MemoryEventTypes.ProceduralRoutineUpserted => "procedure-optimization",
            _ => "context-refinement"
        };

    private static bool TryGetSessionId(string payloadJson, out string sessionId)
    {
        sessionId = string.Empty;
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("sessionId", out var token))
            {
                var value = token.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    sessionId = value.Trim();
                    return true;
                }
            }

            if (root.TryGetProperty("subject", out var subjectToken))
            {
                var subject = subjectToken.GetString();
                if (!string.IsNullOrWhiteSpace(subject) && subject.StartsWith("session:", StringComparison.OrdinalIgnoreCase))
                {
                    sessionId = subject["session:".Length..].Trim();
                    return sessionId.Length > 0;
                }
            }
        }
        catch
        {
            // ignore malformed payloads
        }

        return false;
    }
}
