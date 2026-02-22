namespace CognitiveMemory.Application.Cognitive;

public interface ICompanionCognitiveProfileResolver
{
    Task<ResolvedCompanionCognitiveProfile> ResolveByCompanionIdAsync(Guid companionId, CancellationToken cancellationToken = default);
    Task<ResolvedCompanionCognitiveProfile> ResolveBySessionIdAsync(string sessionId, CancellationToken cancellationToken = default);
}
