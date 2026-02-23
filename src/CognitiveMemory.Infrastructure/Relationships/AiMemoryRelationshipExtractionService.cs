using System.Text.Json;
using CognitiveMemory.Application.Abstractions;
using CognitiveMemory.Application.Relationships;
using CognitiveMemory.Domain.Memory;
using CognitiveMemory.Infrastructure.Persistence;
using CognitiveMemory.Infrastructure.SemanticKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace CognitiveMemory.Infrastructure.Relationships;

public sealed class AiMemoryRelationshipExtractionService(
    ClaimExtractionKernel claimExtractionKernel,
    IMemoryRelationshipRepository relationshipRepository,
    IRelationshipConfidencePolicy confidencePolicy,
    MemoryDbContext dbContext,
    ILogger<AiMemoryRelationshipExtractionService> logger) : IMemoryRelationshipExtractionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const int MaxRelationshipsPerRun = 64;

    public async Task<MemoryRelationshipExtractionResult> ExtractAsync(
        string sessionId,
        int take = 200,
        bool apply = true,
        CancellationToken cancellationToken = default)
    {
        var normalizedSessionId = sessionId.Trim();
        if (normalizedSessionId.Length == 0)
        {
            throw new ArgumentException("sessionId is required.", nameof(sessionId));
        }

        var boundedTake = Math.Clamp(take, 20, 2000);
        var semantic = await dbContext.SemanticClaims
            .AsNoTracking()
            .Where(x => x.Subject.StartsWith($"session:{normalizedSessionId}"))
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(Math.Clamp(boundedTake, 20, 700))
            .ToListAsync(cancellationToken);
        var episodic = await dbContext.EpisodicMemoryEvents
            .AsNoTracking()
            .Where(x => x.SessionId == normalizedSessionId)
            .OrderByDescending(x => x.OccurredAt)
            .Take(Math.Clamp(boundedTake, 20, 700))
            .ToListAsync(cancellationToken);
        var procedural = await dbContext.ProceduralRoutines
            .AsNoTracking()
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(Math.Clamp(boundedTake / 3, 10, 200))
            .ToListAsync(cancellationToken);
        var self = await dbContext.SelfPreferences
            .AsNoTracking()
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(Math.Clamp(boundedTake / 3, 10, 200))
            .ToListAsync(cancellationToken);

        var candidatesScanned = semantic.Count + episodic.Count + procedural.Count + self.Count;
        if (candidatesScanned == 0)
        {
            return new MemoryRelationshipExtractionResult(normalizedSessionId, 0, 0, 0, 0, apply, "No candidate memory rows found.");
        }

        var semanticIds = semantic.Select(x => x.ClaimId.ToString("N")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var episodicIds = episodic.Select(x => x.EventId.ToString("N")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var proceduralIds = procedural.Select(x => x.RoutineId.ToString("N")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selfIds = self.Select(x => x.Key.Trim().ToLowerInvariant()).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var prompt = BuildPrompt(normalizedSessionId, semantic, episodic, procedural, self);
        string? raw;
        try
        {
            var result = await claimExtractionKernel.Value.InvokePromptAsync(prompt, cancellationToken: cancellationToken);
            raw = result.GetValue<string>();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "AI relationship extraction model call failed. SessionId={SessionId}", normalizedSessionId);
            return new MemoryRelationshipExtractionResult(normalizedSessionId, candidatesScanned, 0, 0, 0, apply, "Model call failed.");
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return new MemoryRelationshipExtractionResult(normalizedSessionId, candidatesScanned, 0, 0, 0, apply, "Model returned empty output.");
        }

        var parsed = DeserializeLenient<RelationshipExtractionEnvelope>(raw);
        if (parsed?.Relationships is not { Count: > 0 })
        {
            return new MemoryRelationshipExtractionResult(normalizedSessionId, candidatesScanned, 0, 0, 0, apply, "No relationships proposed.");
        }

        var proposed = 0;
        var appliedCount = 0;
        var rejected = 0;

        foreach (var edge in parsed.Relationships.Take(MaxRelationshipsPerRun))
        {
            if (!TryParseNodeType(edge.FromType, out var fromType) || !TryParseNodeType(edge.ToType, out var toType))
            {
                rejected += 1;
                continue;
            }

            var fromId = (edge.FromId ?? string.Empty).Trim();
            var toId = (edge.ToId ?? string.Empty).Trim();
            var resolvedPolicy = confidencePolicy.Resolve(edge.RelationshipType ?? string.Empty, edge.Confidence, edge.Strength);
            if (fromId.Length == 0 || toId.Length == 0 || resolvedPolicy.RelationshipType.Length == 0)
            {
                rejected += 1;
                continue;
            }

            if (!resolvedPolicy.Accepted)
            {
                rejected += 1;
                continue;
            }

            if (!IsValidNodeReference(fromType, fromId, semanticIds, episodicIds, proceduralIds, selfIds)
                || !IsValidNodeReference(toType, toId, semanticIds, episodicIds, proceduralIds, selfIds))
            {
                rejected += 1;
                continue;
            }

            if (fromType == toType && fromId.Equals(toId, StringComparison.OrdinalIgnoreCase))
            {
                rejected += 1;
                continue;
            }

            proposed += 1;
            if (!apply)
            {
                continue;
            }

            await relationshipRepository.UpsertAsync(
                new MemoryRelationship(
                    Guid.NewGuid(),
                    normalizedSessionId,
                    fromType,
                    NormalizeNodeId(fromType, fromId),
                    toType,
                    NormalizeNodeId(toType, toId),
                    resolvedPolicy.RelationshipType,
                    resolvedPolicy.Confidence,
                    resolvedPolicy.Strength,
                    MemoryRelationshipStatus.Active,
                    null,
                    null,
                    string.IsNullOrWhiteSpace(edge.Reason) ? null : JsonSerializer.Serialize(new { reason = edge.Reason }, JsonOptions),
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow),
                cancellationToken);
            appliedCount += 1;
        }

        return new MemoryRelationshipExtractionResult(
            normalizedSessionId,
            candidatesScanned,
            proposed,
            appliedCount,
            rejected,
            apply);
    }

    private static string BuildPrompt(
        string sessionId,
        IReadOnlyList<Persistence.Entities.SemanticClaimEntity> semantic,
        IReadOnlyList<Persistence.Entities.EpisodicMemoryEventEntity> episodic,
        IReadOnlyList<Persistence.Entities.ProceduralRoutineEntity> procedural,
        IReadOnlyList<Persistence.Entities.SelfPreferenceEntity> self)
    {
        var lines = new List<string>(semantic.Count + episodic.Count + procedural.Count + self.Count + 32)
        {
            "You are a memory relationship extraction model.",
            "Infer explicit, useful edges across memory nodes for the SAME session context.",
            "Allowed node types: SemanticClaim, EpisodicEvent, ProceduralRoutine, SelfPreference.",
            "Allowed relationship types: supports, contradicts, superseded_by, depends_on, about, follows_after, explains, derived_from.",
            "Return strict JSON only:",
            "{\"relationships\":[{\"fromType\":\"...\",\"fromId\":\"...\",\"toType\":\"...\",\"toId\":\"...\",\"relationshipType\":\"...\",\"confidence\":0.0,\"strength\":0.0,\"reason\":\"...\"}]}",
            "Rules:",
            "- Use only ids provided below.",
            "- No markdown.",
            "- No duplicate edges.",
            "- Prefer high-precision edges over high-recall guesses.",
            $"SessionId: {sessionId}",
            "Memory nodes:"
        };

        foreach (var s in semantic)
        {
            lines.Add($"SemanticClaim|{s.ClaimId:N}|{Truncate($"{s.Subject} {s.Predicate} {s.Value}", 260)}");
        }

        foreach (var e in episodic)
        {
            lines.Add($"EpisodicEvent|{e.EventId:N}|{Truncate($"{e.Who} {e.What} {e.Context}", 260)}");
        }

        foreach (var p in procedural)
        {
            lines.Add($"ProceduralRoutine|{p.RoutineId:N}|{Truncate($"{p.Trigger} {p.Name} {p.Outcome}", 260)}");
        }

        foreach (var sp in self)
        {
            lines.Add($"SelfPreference|{sp.Key.Trim().ToLowerInvariant()}|{Truncate(sp.Value, 260)}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static bool TryParseNodeType(string? raw, out MemoryNodeType type)
    {
        type = default;
        return Enum.TryParse(raw?.Trim(), true, out type)
               && (type is MemoryNodeType.SemanticClaim
                   or MemoryNodeType.EpisodicEvent
                   or MemoryNodeType.ProceduralRoutine
                   or MemoryNodeType.SelfPreference);
    }

    private static bool IsValidNodeReference(
        MemoryNodeType type,
        string id,
        HashSet<string> semanticIds,
        HashSet<string> episodicIds,
        HashSet<string> proceduralIds,
        HashSet<string> selfIds)
    {
        var normalized = NormalizeNodeId(type, id);
        return type switch
        {
            MemoryNodeType.SemanticClaim => semanticIds.Contains(normalized),
            MemoryNodeType.EpisodicEvent => episodicIds.Contains(normalized),
            MemoryNodeType.ProceduralRoutine => proceduralIds.Contains(normalized),
            MemoryNodeType.SelfPreference => selfIds.Contains(normalized),
            _ => false
        };
    }

    private static string NormalizeNodeId(MemoryNodeType type, string id)
    {
        var trimmed = id.Trim();
        if (type == MemoryNodeType.SelfPreference)
        {
            return trimmed.ToLowerInvariant();
        }

        return Guid.TryParse(trimmed, out var parsed) ? parsed.ToString("N") : trimmed.ToLowerInvariant();
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];

    private static T? DeserializeLenient<T>(string raw)
    {
        var candidate = raw.Trim();
        if (candidate.StartsWith("```", StringComparison.Ordinal))
        {
            candidate = candidate
                .Replace("```json", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("```", string.Empty, StringComparison.Ordinal)
                .Trim();
        }

        var start = candidate.IndexOf('{');
        var end = candidate.LastIndexOf('}');
        if (start >= 0 && end >= start)
        {
            candidate = candidate[start..(end + 1)];
        }

        try
        {
            return JsonSerializer.Deserialize<T>(candidate, JsonOptions);
        }
        catch
        {
            return default;
        }
    }

    private sealed class RelationshipExtractionEnvelope
    {
        public List<RelationshipCandidate> Relationships { get; set; } = [];
    }

    private sealed class RelationshipCandidate
    {
        public string? FromType { get; set; }
        public string? FromId { get; set; }
        public string? ToType { get; set; }
        public string? ToId { get; set; }
        public string? RelationshipType { get; set; }
        public double? Confidence { get; set; }
        public double? Strength { get; set; }
        public string? Reason { get; set; }
    }
}
