using CognitiveMemory.Domain.Memory;

namespace CognitiveMemory.Application.Abstractions;

public interface IProceduralMemoryRepository
{
    Task<ProceduralRoutine> UpsertAsync(ProceduralRoutine routine, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProceduralRoutine>> QueryByTriggerAsync(string trigger, int take = 20, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProceduralRoutine>> QueryRecentAsync(int take = 50, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProceduralRoutine>> SearchAsync(string query, int take = 20, CancellationToken cancellationToken = default);
}
