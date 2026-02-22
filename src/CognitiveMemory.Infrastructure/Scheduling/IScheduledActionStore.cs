using CognitiveMemory.Infrastructure.Persistence.Entities;

namespace CognitiveMemory.Infrastructure.Scheduling;

public interface IScheduledActionStore
{
    Task<ScheduledActionEntity> ScheduleAsync(
        string sessionId,
        string actionType,
        string inputJson,
        DateTimeOffset runAtUtc,
        int maxAttempts,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ScheduledActionEntity>> ListAsync(
        string? sessionId,
        string? status,
        int take,
        CancellationToken cancellationToken = default);

    Task<bool> CancelAsync(Guid actionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ScheduledActionEntity>> ClaimDueAsync(
        DateTimeOffset nowUtc,
        int batchSize,
        CancellationToken cancellationToken = default);

    Task MarkCompletedAsync(Guid actionId, CancellationToken cancellationToken = default);

    Task MarkFailedAsync(Guid actionId, string error, bool exhaustedRetries, CancellationToken cancellationToken = default);
}
