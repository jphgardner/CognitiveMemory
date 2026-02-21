using CognitiveMemory.Domain.Memory;

namespace CognitiveMemory.Application.Episodic;

public interface IEpisodicMemoryService
{
    Task<EpisodicMemoryEvent> AppendAsync(AppendEpisodicMemoryRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EpisodicMemoryEvent>> QueryBySessionAsync(
        string sessionId,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        int take = 100,
        CancellationToken cancellationToken = default);
}
