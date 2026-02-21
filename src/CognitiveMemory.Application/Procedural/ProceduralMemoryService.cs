using CognitiveMemory.Application.Abstractions;
using CognitiveMemory.Domain.Memory;

namespace CognitiveMemory.Application.Procedural;

public sealed class ProceduralMemoryService(IProceduralMemoryRepository repository) : IProceduralMemoryService
{
    public async Task<ProceduralRoutine> UpsertAsync(UpsertProceduralRoutineRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Trigger)) throw new ArgumentException("Trigger is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Name)) throw new ArgumentException("Name is required.", nameof(request));
        if (request.Steps.Count == 0) throw new ArgumentException("At least one step is required.", nameof(request));

        var now = DateTimeOffset.UtcNow;
        var routine = new ProceduralRoutine(
            request.RoutineId ?? Guid.NewGuid(),
            request.Trigger.Trim(),
            request.Name.Trim(),
            request.Steps.Select(x => x.Trim()).Where(x => x.Length > 0).ToArray(),
            request.Checkpoints.Select(x => x.Trim()).Where(x => x.Length > 0).ToArray(),
            request.Outcome.Trim(),
            now,
            now);

        return await repository.UpsertAsync(routine, cancellationToken);
    }

    public Task<IReadOnlyList<ProceduralRoutine>> QueryByTriggerAsync(string trigger, int take = 20, CancellationToken cancellationToken = default)
        => repository.QueryByTriggerAsync(trigger.Trim(), Math.Clamp(take, 1, 100), cancellationToken);
}
