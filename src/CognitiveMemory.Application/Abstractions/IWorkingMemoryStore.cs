namespace CognitiveMemory.Application.Abstractions;

public interface IWorkingMemoryStore
{
    Task<WorkingMemoryContext> GetAsync(string sessionId, CancellationToken cancellationToken = default);
    Task SaveAsync(WorkingMemoryContext context, CancellationToken cancellationToken = default);
}

public sealed record WorkingMemoryContext(string SessionId, IReadOnlyList<WorkingMemoryTurn> Turns);
public sealed record WorkingMemoryTurn(string Role, string Content, DateTimeOffset CreatedAtUtc);
