namespace CognitiveMemory.Infrastructure.Subconscious;

public interface ISubconsciousOutcomeApplier
{
    Task<SubconsciousApplyReport> ApplyAsync(Guid debateId, string sessionId, SubconsciousDebateOutcome outcome, CancellationToken cancellationToken = default);
    Task<SubconsciousApplyReport> PreviewAsync(Guid debateId, string sessionId, SubconsciousDebateOutcome outcome, CancellationToken cancellationToken = default);
}
