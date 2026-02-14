namespace CognitiveMemory.Application.AI.Tooling;

public sealed class OutboxEventWriteRequest
{
    public string EventType { get; init; } = string.Empty;

    public string AggregateType { get; init; } = string.Empty;

    public Guid? AggregateId { get; init; }

    public string PayloadJson { get; init; } = "{}";

    public string IdempotencyKey { get; init; } = string.Empty;

    public DateTimeOffset? AvailableAt { get; init; }
}

public sealed class OutboxEventRecord
{
    public Guid EventId { get; init; }

    public string EventType { get; init; } = string.Empty;

    public string AggregateType { get; init; } = string.Empty;

    public Guid? AggregateId { get; init; }

    public string PayloadJson { get; init; } = "{}";

    public string IdempotencyKey { get; init; } = string.Empty;

    public int Attempts { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class OutboxStatusSummary
{
    public int Total { get; init; }

    public int Pending { get; init; }

    public int Failed { get; init; }

    public int Processing { get; init; }

    public int Succeeded { get; init; }

    public int StaleProcessing { get; init; }

    public double OldestPendingAgeSeconds { get; init; }
}

public sealed class OutboxEventStatusRecord
{
    public Guid EventId { get; init; }

    public string EventType { get; init; } = string.Empty;

    public string AggregateType { get; init; } = string.Empty;

    public Guid? AggregateId { get; init; }

    public string Status { get; init; } = string.Empty;

    public int Attempts { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset AvailableAt { get; init; }

    public DateTimeOffset? LockedUntil { get; init; }

    public DateTimeOffset? ProcessedAt { get; init; }

    public string? LastError { get; init; }
}

public interface IOutboxRepository
{
    Task<Guid> EnqueueAsync(OutboxEventWriteRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<OutboxEventRecord>> ReservePendingAsync(int batchSize, TimeSpan leaseDuration, CancellationToken cancellationToken);

    Task MarkSucceededAsync(Guid eventId, CancellationToken cancellationToken);

    Task MarkFailedAsync(Guid eventId, string error, TimeSpan retryDelay, CancellationToken cancellationToken);

    Task<OutboxStatusSummary> GetStatusSummaryAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<OutboxEventStatusRecord>> GetRecentAsync(int take, string? status, CancellationToken cancellationToken);
}
