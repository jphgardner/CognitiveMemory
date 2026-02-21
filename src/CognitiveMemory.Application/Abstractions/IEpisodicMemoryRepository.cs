using CognitiveMemory.Domain.Memory;

namespace CognitiveMemory.Application.Abstractions;

public interface IEpisodicMemoryRepository
{
    Task AppendAsync(EpisodicMemoryEvent memoryEvent, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EpisodicMemoryEvent>> QueryBySessionAsync(
        string sessionId,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        int take = 100,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EpisodicMemoryEvent>> SearchBySessionAsync(
        string sessionId,
        string query,
        int take = 100,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EpisodicMemoryEvent>> QueryRangeAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int take = 500,
        CancellationToken cancellationToken = default);
}
