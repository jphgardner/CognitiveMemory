namespace CognitiveMemory.Infrastructure.Subconscious;

public interface ISubconsciousDebateService
{
    Task QueueDebateAsync(string sessionId, SubconsciousDebateTopic topic, CancellationToken cancellationToken = default);
    Task ProcessDebateAsync(Guid debateId, CancellationToken cancellationToken = default);
    Task<bool> ApproveDebateAsync(Guid debateId, CancellationToken cancellationToken = default);
    Task<bool> RejectDebateAsync(Guid debateId, CancellationToken cancellationToken = default);
}
