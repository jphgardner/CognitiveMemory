using CognitiveMemory.Application.Abstractions;
using CognitiveMemory.Domain.Memory;
using CognitiveMemory.Infrastructure.Persistence;
using CognitiveMemory.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace CognitiveMemory.Infrastructure.Repositories;

public sealed class ToolInvocationAuditRepository(MemoryDbContext dbContext) : IToolInvocationAuditRepository
{
    public async Task AddAsync(ToolInvocationAudit audit, CancellationToken cancellationToken = default)
    {
        dbContext.ToolInvocationAudits.Add(
            new ToolInvocationAuditEntity
            {
                AuditId = audit.AuditId,
                ToolName = audit.ToolName,
                IsWrite = audit.IsWrite,
                ArgumentsJson = audit.ArgumentsJson,
                ResultJson = audit.ResultJson,
                Succeeded = audit.Succeeded,
                Error = audit.Error,
                ExecutedAtUtc = audit.ExecutedAtUtc
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
}
