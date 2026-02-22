using CognitiveMemory.Application.Abstractions;
using CognitiveMemory.Domain.Memory;
using CognitiveMemory.Infrastructure.Events;
using CognitiveMemory.Infrastructure.Persistence;
using CognitiveMemory.Infrastructure.Companions;
using CognitiveMemory.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CognitiveMemory.Infrastructure.Repositories;

public sealed class ToolInvocationAuditRepository(MemoryDbContext dbContext, IOutboxWriter outboxWriter, ICompanionScopeResolver companionScopeResolver) : IToolInvocationAuditRepository
{
    public async Task AddAsync(ToolInvocationAudit audit, CancellationToken cancellationToken = default)
    {
        var companionId = await ResolveCompanionIdFromArgumentsAsync(audit.ArgumentsJson, cancellationToken);
        dbContext.ToolInvocationAudits.Add(
            new ToolInvocationAuditEntity
            {
                AuditId = audit.AuditId,
                CompanionId = companionId,
                ToolName = audit.ToolName,
                IsWrite = audit.IsWrite,
                ArgumentsJson = audit.ArgumentsJson,
                ResultJson = audit.ResultJson,
                Succeeded = audit.Succeeded,
                    Error = audit.Error,
                    ExecutedAtUtc = audit.ExecutedAtUtc
            });

        outboxWriter.Enqueue(
            MemoryEventTypes.ToolInvocationCompleted,
            aggregateType: "ToolInvocationAudit",
            aggregateId: audit.AuditId.ToString("N"),
            payload: new
            {
                companionId,
                audit.AuditId,
                audit.ToolName,
                audit.IsWrite,
                audit.Succeeded,
                audit.ExecutedAtUtc,
                audit.Error
            });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ToolInvocationAudit>> QueryByExecutedAtRangeAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        var normalizedTake = Math.Clamp(take, 1, 200);
        var from = fromUtc <= toUtc ? fromUtc : toUtc;
        var to = toUtc >= fromUtc ? toUtc : fromUtc;

        var rows = await dbContext.ToolInvocationAudits
            .AsNoTracking()
            .Where(x => x.ExecutedAtUtc >= from && x.ExecutedAtUtc <= to)
            .OrderByDescending(x => x.ExecutedAtUtc)
            .Take(normalizedTake)
            .ToListAsync(cancellationToken);

        return rows
            .Select(
                x => new ToolInvocationAudit(
                    x.AuditId,
                    x.ToolName,
                    x.IsWrite,
                    x.ArgumentsJson,
                    x.ResultJson,
                    x.Succeeded,
                    x.Error,
                    x.ExecutedAtUtc))
            .ToArray();
    }

    private async Task<Guid> ResolveCompanionIdFromArgumentsAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
        {
            return Guid.Empty;
        }

        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Guid.Empty;
            }

            if (doc.RootElement.TryGetProperty("sessionId", out var sessionProp))
            {
                var sessionId = sessionProp.GetString();
                if (!string.IsNullOrWhiteSpace(sessionId))
                {
                    return await companionScopeResolver.ResolveCompanionIdOrThrowAsync(sessionId.Trim(), cancellationToken);
                }
            }
        }
        catch
        {
            // keep audit even when scope cannot be resolved
        }

        return Guid.Empty;
    }
}
