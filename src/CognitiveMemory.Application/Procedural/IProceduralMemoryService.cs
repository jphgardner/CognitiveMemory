using CognitiveMemory.Domain.Memory;

namespace CognitiveMemory.Application.Procedural;

public interface IProceduralMemoryService
{
    Task<ProceduralRoutine> UpsertAsync(UpsertProceduralRoutineRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProceduralRoutine>> QueryByTriggerAsync(string trigger, int take = 20, CancellationToken cancellationToken = default);
}
