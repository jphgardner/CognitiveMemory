using System.Text.Json;
using CognitiveMemory.Infrastructure.Events;
using CognitiveMemory.Infrastructure.Persistence;
using CognitiveMemory.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CognitiveMemory.Infrastructure.Reactive;

public sealed class MemoryConflictEscalationConsumer(
    MemoryDbContext dbContext,
    ILogger<MemoryConflictEscalationConsumer> logger) : IOutboxEventConsumer
{
    public string ConsumerName => nameof(MemoryConflictEscalationConsumer);

    public bool CanHandle(string eventType)
        => string.Equals(eventType, MemoryEventTypes.SemanticContradictionAdded, StringComparison.Ordinal);

    public async Task HandleAsync(OutboxEvent @event, CancellationToken cancellationToken = default)
    {
        if (!TryGetIds(@event.PayloadJson, out var claimAId, out var claimBId))
        {
            return;
        }

        var claims = await dbContext.SemanticClaims
            .AsNoTracking()
            .Where(x => x.ClaimId == claimAId || x.ClaimId == claimBId)
            .ToArrayAsync(cancellationToken);

        if (claims.Length < 2)
        {
            return;
        }

        var companionId = claims[0].CompanionId;
        if (claims.Any(x => x.CompanionId != companionId))
        {
            return;
        }

        var subject = claims[0].Subject;
        var predicate = claims[0].Predicate;
        var contradictionCount = await dbContext.ClaimContradictions
            .AsNoTracking()
            .Join(
                dbContext.SemanticClaims.AsNoTracking(),
                c => c.ClaimAId,
                s => s.ClaimId,
                (c, s) => new { Contradiction = c, Claim = s })
            .Where(
                x => x.Claim.CompanionId == companionId
                     && x.Claim.Subject == subject
                     && x.Claim.Predicate == predicate)
            .CountAsync(cancellationToken);

        if (contradictionCount < 3)
        {
            return;
        }

        var values = claims.Select(x => x.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var existing = await dbContext.ConflictEscalationAlerts
            .FirstOrDefaultAsync(
                x => x.CompanionId == companionId
                     && x.Subject == subject
                     && x.Predicate == predicate
                     && x.Status == "Open",
                cancellationToken);

        if (existing is null)
        {
            dbContext.ConflictEscalationAlerts.Add(
                new ConflictEscalationAlertEntity
                {
                    AlertId = Guid.NewGuid(),
                    CompanionId = companionId,
                    Subject = subject,
                    Predicate = predicate,
                    ValuesJson = JsonSerializer.Serialize(values),
                    ContradictionCount = contradictionCount,
                    Status = "Open",
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    LastSeenAtUtc = DateTimeOffset.UtcNow
                });
        }
        else
        {
            existing.ValuesJson = JsonSerializer.Serialize(values);
            existing.ContradictionCount = contradictionCount;
            existing.LastSeenAtUtc = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogWarning(
            "Conflict escalation alert updated. Subject={Subject} Predicate={Predicate} Contradictions={Count}",
            subject,
            predicate,
            contradictionCount);
    }

    private static bool TryGetIds(string payloadJson, out Guid claimAId, out Guid claimBId)
    {
        claimAId = Guid.Empty;
        claimBId = Guid.Empty;
        try
        {
            using var json = JsonDocument.Parse(payloadJson);
            var root = json.RootElement;
            if (!root.TryGetProperty("claimAId", out var a) || !root.TryGetProperty("claimBId", out var b))
            {
                return false;
            }

            claimAId = a.ValueKind == JsonValueKind.String ? Guid.Parse(a.GetString()!) : a.GetGuid();
            claimBId = b.ValueKind == JsonValueKind.String ? Guid.Parse(b.GetString()!) : b.GetGuid();
            return claimAId != Guid.Empty && claimBId != Guid.Empty;
        }
        catch
        {
            return false;
        }
    }
}
