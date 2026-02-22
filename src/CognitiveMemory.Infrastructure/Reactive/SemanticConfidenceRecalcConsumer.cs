using System.Text.Json;
using CognitiveMemory.Domain.Memory;
using CognitiveMemory.Infrastructure.Events;
using CognitiveMemory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CognitiveMemory.Infrastructure.Reactive;

public sealed class SemanticConfidenceRecalcConsumer(
    MemoryDbContext dbContext,
    ILogger<SemanticConfidenceRecalcConsumer> logger) : IOutboxEventConsumer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string ConsumerName => nameof(SemanticConfidenceRecalcConsumer);

    public bool CanHandle(string eventType)
        => eventType is MemoryEventTypes.SemanticEvidenceAdded or MemoryEventTypes.SemanticContradictionAdded;

    public async Task HandleAsync(OutboxEvent @event, CancellationToken cancellationToken = default)
    {
        var claimId = TryResolveClaimId(@event);
        if (claimId == Guid.Empty)
        {
            return;
        }

        var claim = await dbContext.SemanticClaims.FirstOrDefaultAsync(x => x.ClaimId == claimId, cancellationToken);
        if (claim is null || !string.Equals(claim.Status, SemanticClaimStatus.Active.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var original = claim.Confidence;
        var adjusted = claim.Confidence;
        if (string.Equals(@event.EventType, MemoryEventTypes.SemanticEvidenceAdded, StringComparison.Ordinal))
        {
            adjusted = Math.Min(0.99, adjusted + 0.03);
        }
        else
        {
            adjusted = Math.Max(0.05, adjusted - 0.08);
        }

        if (Math.Abs(adjusted - original) < 0.01)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var replacement = new Persistence.Entities.SemanticClaimEntity
        {
            ClaimId = Guid.NewGuid(),
            Subject = claim.Subject,
            Predicate = claim.Predicate,
            Value = claim.Value,
            Confidence = adjusted,
            Scope = claim.Scope,
            Status = SemanticClaimStatus.Active.ToString(),
            ValidFromUtc = claim.ValidFromUtc,
            ValidToUtc = null,
            SupersededByClaimId = null,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        claim.Status = SemanticClaimStatus.Superseded.ToString();
        claim.SupersededByClaimId = replacement.ClaimId;
        claim.ValidToUtc = now;
        claim.UpdatedAtUtc = now;

        dbContext.SemanticClaims.Add(replacement);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Semantic confidence recalculated. EventType={EventType} ClaimId={ClaimId} NewClaimId={NewClaimId} Old={OldConfidence} New={NewConfidence}",
            @event.EventType,
            claimId,
            replacement.ClaimId,
            original,
            adjusted);
    }

    private static Guid TryResolveClaimId(OutboxEvent @event)
    {
        try
        {
            using var json = JsonDocument.Parse(@event.PayloadJson);
            if (json.RootElement.TryGetProperty("claimId", out var claimId))
            {
                return claimId.ValueKind == JsonValueKind.String
                    ? Guid.TryParse(claimId.GetString(), out var parsed) ? parsed : Guid.Empty
                    : claimId.GetGuid();
            }

            if (json.RootElement.TryGetProperty("claimAId", out var claimAId))
            {
                return claimAId.ValueKind == JsonValueKind.String
                    ? Guid.TryParse(claimAId.GetString(), out var parsed) ? parsed : Guid.Empty
                    : claimAId.GetGuid();
            }
        }
        catch
        {
            // ignored
        }

        return Guid.Empty;
    }
}
