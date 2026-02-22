namespace CognitiveMemory.Application.Abstractions;

public interface ICompanionDirectory
{
    Task<IReadOnlyList<CompanionScope>> ListActiveAsync(CancellationToken cancellationToken = default);
    Task<CompanionScope?> GetByCompanionIdAsync(Guid companionId, CancellationToken cancellationToken = default);
}

public sealed record CompanionScope(Guid CompanionId, string SessionId, string UserId);
