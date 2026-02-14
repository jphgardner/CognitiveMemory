namespace CognitiveMemory.Application.AI.Tooling;

public sealed class ToolExecutionRecord
{
    public Guid ExecutionId { get; init; }

    public string ToolName { get; init; } = string.Empty;

    public string IdempotencyKey { get; init; } = string.Empty;

    public string ResponseJson { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}

public interface IToolExecutionRepository
{
    Task<ToolExecutionRecord?> GetAsync(string toolName, string idempotencyKey, CancellationToken cancellationToken);

    Task SaveAsync(string toolName, string idempotencyKey, string responseJson, CancellationToken cancellationToken);
}
