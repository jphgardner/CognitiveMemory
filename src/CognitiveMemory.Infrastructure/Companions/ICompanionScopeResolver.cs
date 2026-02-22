namespace CognitiveMemory.Infrastructure.Companions;

public interface ICompanionScopeResolver
{
    Task<Guid?> TryResolveCompanionIdAsync(string sessionId, CancellationToken cancellationToken = default);
    Task<Guid> ResolveCompanionIdOrThrowAsync(string sessionId, CancellationToken cancellationToken = default);
}
