using CognitiveMemory.Application.Abstractions;
using CognitiveMemory.Domain.Memory;

namespace CognitiveMemory.Application.Episodic;

public sealed class EpisodicMemoryService(IEpisodicMemoryRepository repository) : IEpisodicMemoryService
{
    public async Task<EpisodicMemoryEvent> AppendAsync(
        AppendEpisodicMemoryRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId))
        {
            throw new ArgumentException("SessionId is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Who))
        {
            throw new ArgumentException("Who is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.What))
        {
            throw new ArgumentException("What is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.SourceReference))
        {
            throw new ArgumentException("SourceReference is required.", nameof(request));
        }

        var memoryEvent = new EpisodicMemoryEvent(
            Guid.NewGuid(),
            request.SessionId.Trim(),
            request.Who.Trim(),
            request.What.Trim(),
            request.OccurredAtUtc ?? DateTimeOffset.UtcNow,
            request.Context.Trim(),
            request.SourceReference.Trim());

        await repository.AppendAsync(memoryEvent, cancellationToken);
        return memoryEvent;
    }

    public Task<IReadOnlyList<EpisodicMemoryEvent>> QueryBySessionAsync(
        string sessionId,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("SessionId is required.", nameof(sessionId));
        }

        var normalizedTake = Math.Clamp(take, 1, 500);
        return repository.QueryBySessionAsync(sessionId.Trim(), fromUtc, toUtc, normalizedTake, cancellationToken);
    }
}
