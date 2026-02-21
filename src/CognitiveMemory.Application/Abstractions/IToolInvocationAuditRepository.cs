using CognitiveMemory.Domain.Memory;

namespace CognitiveMemory.Application.Abstractions;

public interface IToolInvocationAuditRepository
{
    Task AddAsync(ToolInvocationAudit audit, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ToolInvocationAudit>> QueryByExecutedAtRangeAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int take = 50,
        CancellationToken cancellationToken = default);
}
