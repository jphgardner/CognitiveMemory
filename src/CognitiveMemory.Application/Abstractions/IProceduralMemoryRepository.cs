using CognitiveMemory.Domain.Memory;

namespace CognitiveMemory.Application.Abstractions;

public interface IProceduralMemoryRepository
{
    Task<ProceduralRoutine> UpsertAsync(ProceduralRoutine routine, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProceduralRoutine>> QueryByTriggerAsync(string trigger, int take = 20, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProceduralRoutine>> QueryRecentAsync(int take = 50, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProceduralRoutine>> SearchAsync(string query, int take = 20, CancellationToken cancellationToken = default);
    Task<ProceduralRoutine> UpsertAsync(Guid companionId, ProceduralRoutine routine, CancellationToken cancellationToken = default)
        => UpsertAsync(routine, cancellationToken);
    Task<IReadOnlyList<ProceduralRoutine>> QueryByTriggerAsync(Guid companionId, string trigger, int take = 20, CancellationToken cancellationToken = default)
        => QueryByTriggerAsync(trigger, take, cancellationToken);
    Task<IReadOnlyList<ProceduralRoutine>> QueryRecentAsync(Guid companionId, int take = 50, CancellationToken cancellationToken = default)
        => QueryRecentAsync(take, cancellationToken);
    Task<IReadOnlyList<ProceduralRoutine>> SearchAsync(Guid companionId, string query, int take = 20, CancellationToken cancellationToken = default)
        => SearchAsync(query, take, cancellationToken);
}
