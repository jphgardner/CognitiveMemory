using System.Text.Json;
using CognitiveMemory.Application.Abstractions;
using CognitiveMemory.Domain.Memory;
using CognitiveMemory.Infrastructure.Events;
using CognitiveMemory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CognitiveMemory.Infrastructure.Reactive;

public sealed class MemoryRelationshipAutoLinkConsumer(
    IMemoryRelationshipRepository relationshipRepository,
    MemoryDbContext dbContext,
    ILogger<MemoryRelationshipAutoLinkConsumer> logger) : IOutboxEventConsumer
{
    public string ConsumerName => nameof(MemoryRelationshipAutoLinkConsumer);

    public bool CanHandle(string eventType)
        => eventType is MemoryEventTypes.SemanticClaimCreated
            or MemoryEventTypes.SelfPreferenceSet
            or MemoryEventTypes.ProceduralRoutineUpserted
            or MemoryEventTypes.SemanticEvidenceAdded
            or MemoryEventTypes.SemanticContradictionAdded;

    public async Task HandleAsync(OutboxEvent @event, CancellationToken cancellationToken = default)
    {
        try
        {
            using var doc = JsonDocument.Parse(@event.PayloadJson);
            var root = doc.RootElement;

            if (@event.EventType == MemoryEventTypes.SemanticClaimCreated)
            {
                await HandleSemanticCreatedAsync(root, cancellationToken);
                return;
            }

            if (@event.EventType == MemoryEventTypes.SelfPreferenceSet)
            {
                await HandleSelfPreferenceSetAsync(root, cancellationToken);
                return;
            }

            if (@event.EventType == MemoryEventTypes.ProceduralRoutineUpserted)
            {
                await HandleProceduralUpsertedAsync(root, cancellationToken);
                return;
            }

            if (@event.EventType == MemoryEventTypes.SemanticEvidenceAdded)
            {
                await HandleSemanticEvidenceAddedAsync(root, cancellationToken);
                return;
            }

            if (@event.EventType == MemoryEventTypes.SemanticContradictionAdded)
            {
                await HandleSemanticContradictionAddedAsync(root, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Memory relationship auto-link skipped for event {EventType}", @event.EventType);
        }
    }

    private async Task HandleSemanticCreatedAsync(JsonElement root, CancellationToken cancellationToken)
    {
        if (!TryParseGuid(root, "ClaimId", "claimId", out var claimId))
        {
            return;
        }

        _ = TryParseGuid(root, "CompanionId", "companionId", out var companionId);
        var subject = GetString(root, "Subject", "subject");
        var predicate = GetString(root, "Predicate", "predicate");
        var sessionId = await ResolveSessionIdAsync(companionId == Guid.Empty ? null : companionId, subject, cancellationToken);
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(predicate) && predicate.StartsWith("identity.", StringComparison.OrdinalIgnoreCase))
        {
            await relationshipRepository.UpsertAsync(
                new MemoryRelationship(
                    Guid.NewGuid(),
                    sessionId,
                    MemoryNodeType.SemanticClaim,
                    claimId.ToString("N"),
                    MemoryNodeType.SelfPreference,
                    predicate.Trim().ToLowerInvariant(),
                    "describes_identity",
                    0.8,
                    0.75,
                    MemoryRelationshipStatus.Active,
                    null,
                    null,
                    null,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow),
                cancellationToken);
        }
    }

    private async Task HandleSelfPreferenceSetAsync(JsonElement root, CancellationToken cancellationToken)
    {
        // Intentionally no-op:
        // `SelfPreference(key) -> SelfPreference(key)` produces self-loop edges that add no graph value.
        await Task.CompletedTask;
    }

    private async Task HandleProceduralUpsertedAsync(JsonElement root, CancellationToken cancellationToken)
    {
        if (!TryParseGuid(root, "RoutineId", "routineId", out var routineId))
        {
            return;
        }

        var trigger = GetString(root, "Trigger", "trigger");
        if (string.IsNullOrWhiteSpace(trigger))
        {
            return;
        }

        _ = TryParseGuid(root, "CompanionId", "companionId", out var companionId);
        var sessionId = await ResolveSessionIdAsync(companionId == Guid.Empty ? null : companionId, null, cancellationToken);
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        await relationshipRepository.UpsertAsync(
            new MemoryRelationship(
                Guid.NewGuid(),
                sessionId,
                MemoryNodeType.ProceduralRoutine,
                routineId.ToString("N"),
                MemoryNodeType.SelfPreference,
                $"trigger:{trigger.Trim().ToLowerInvariant()}",
                "driven_by_trigger",
                0.65,
                0.6,
                MemoryRelationshipStatus.Active,
                null,
                null,
                null,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow),
            cancellationToken);
    }

    private async Task HandleSemanticEvidenceAddedAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var claimIdText = GetString(root, "ClaimId", "claimId");
        if (!Guid.TryParse(claimIdText, out var claimId))
        {
            return;
        }

        var sourceType = GetString(root, "SourceType", "sourceType").Trim().ToLowerInvariant();
        var sourceReference = GetString(root, "SourceReference", "sourceReference").Trim();
        if (string.IsNullOrWhiteSpace(sourceType) || string.IsNullOrWhiteSpace(sourceReference))
        {
            return;
        }

        _ = TryParseGuid(root, "CompanionId", "companionId", out var payloadCompanionId);
        var claim = await dbContext.SemanticClaims.AsNoTracking().FirstOrDefaultAsync(x => x.ClaimId == claimId, cancellationToken);
        var companionId = claim?.CompanionId ?? (payloadCompanionId == Guid.Empty ? (Guid?)null : payloadCompanionId);
        var sessionId = await ResolveSessionIdAsync(companionId, claim?.Subject, cancellationToken);
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        if (sourceType.Contains("episodic", StringComparison.Ordinal)
            && TryExtractGuid(sourceReference, out var eventId))
        {
            await relationshipRepository.UpsertAsync(
                new MemoryRelationship(
                    Guid.NewGuid(),
                    sessionId,
                    MemoryNodeType.SemanticClaim,
                    claimId.ToString("N"),
                    MemoryNodeType.EpisodicEvent,
                    eventId.ToString("N"),
                    "supported_by",
                    0.86,
                    0.82,
                    MemoryRelationshipStatus.Active,
                    null,
                    null,
                    null,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow),
                cancellationToken);
            return;
        }

        if (sourceType.Contains("procedural", StringComparison.Ordinal)
            && TryExtractGuid(sourceReference, out var routineId))
        {
            await relationshipRepository.UpsertAsync(
                new MemoryRelationship(
                    Guid.NewGuid(),
                    sessionId,
                    MemoryNodeType.SemanticClaim,
                    claimId.ToString("N"),
                    MemoryNodeType.ProceduralRoutine,
                    routineId.ToString("N"),
                    "supported_by_routine",
                    0.78,
                    0.72,
                    MemoryRelationshipStatus.Active,
                    null,
                    null,
                    null,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow),
                cancellationToken);
            return;
        }

        if (sourceType.Contains("self", StringComparison.Ordinal))
        {
            await relationshipRepository.UpsertAsync(
                new MemoryRelationship(
                    Guid.NewGuid(),
                    sessionId,
                    MemoryNodeType.SemanticClaim,
                    claimId.ToString("N"),
                    MemoryNodeType.SelfPreference,
                    sourceReference.ToLowerInvariant(),
                    "supported_by_self",
                    0.72,
                    0.7,
                    MemoryRelationshipStatus.Active,
                    null,
                    null,
                    null,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow),
                cancellationToken);
        }
    }

    private async Task HandleSemanticContradictionAddedAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var claimAText = GetString(root, "ClaimAId", "claimAId");
        var claimBText = GetString(root, "ClaimBId", "claimBId");
        if (!Guid.TryParse(claimAText, out var claimAId) || !Guid.TryParse(claimBText, out var claimBId))
        {
            return;
        }

        _ = TryParseGuid(root, "CompanionId", "companionId", out var payloadCompanionId);
        var claimA = await dbContext.SemanticClaims.AsNoTracking().FirstOrDefaultAsync(x => x.ClaimId == claimAId, cancellationToken);
        var companionId = claimA?.CompanionId ?? (payloadCompanionId == Guid.Empty ? (Guid?)null : payloadCompanionId);
        var sessionId = await ResolveSessionIdAsync(companionId, claimA?.Subject, cancellationToken);
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        await relationshipRepository.UpsertAsync(
            new MemoryRelationship(
                Guid.NewGuid(),
                sessionId,
                MemoryNodeType.SemanticClaim,
                claimAId.ToString("N"),
                MemoryNodeType.SemanticClaim,
                claimBId.ToString("N"),
                "contradicts",
                0.92,
                0.9,
                MemoryRelationshipStatus.Active,
                null,
                null,
                null,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow),
            cancellationToken);
    }

    private static string GetString(JsonElement root, string a, string b)
    {
        if (root.TryGetProperty(a, out var upper) && upper.ValueKind == JsonValueKind.String)
        {
            return upper.GetString() ?? string.Empty;
        }

        if (root.TryGetProperty(b, out var lower) && lower.ValueKind == JsonValueKind.String)
        {
            return lower.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static bool TryParseGuid(JsonElement root, string a, string b, out Guid value)
    {
        value = Guid.Empty;
        var text = GetString(root, a, b);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return Guid.TryParse(text, out value);
    }

    private static string? TryExtractSessionId(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            return null;
        }

        var normalized = subject.Trim();
        if (!normalized.StartsWith("session:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var sid = normalized["session:".Length..].Trim();
        return sid.Length == 0 ? null : sid;
    }

    private async Task<string?> ResolveSessionIdAsync(Guid? companionId, string? subject, CancellationToken cancellationToken)
    {
        var parsed = TryExtractSessionId(subject);
        if (!string.IsNullOrWhiteSpace(parsed))
        {
            return parsed;
        }

        if (!companionId.HasValue || companionId.Value == Guid.Empty)
        {
            return null;
        }

        return await dbContext.Companions
            .AsNoTracking()
            .Where(x => !x.IsArchived && x.CompanionId == companionId.Value)
            .Select(x => x.SessionId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static bool TryExtractGuid(string input, out Guid guid)
    {
        guid = Guid.Empty;
        var trimmed = input.Trim();
        if (Guid.TryParse(trimmed, out guid))
        {
            return true;
        }

        var parts = trimmed.Split([':', '/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts.Reverse())
        {
            if (Guid.TryParse(part, out guid))
            {
                return true;
            }
        }

        return false;
    }
}
