using System.ComponentModel.DataAnnotations;

namespace CognitiveMemory.Domain.Entities;

public sealed class OutboxEvent
{
    [Key]
    public Guid EventId { get; set; }

    [MaxLength(128)]
    public string EventType { get; set; } = string.Empty;

    [MaxLength(64)]
    public string AggregateType { get; set; } = string.Empty;

    public Guid? AggregateId { get; set; }

    public string PayloadJson { get; set; } = "{}";

    [MaxLength(256)]
    public string IdempotencyKey { get; set; } = string.Empty;

    [MaxLength(32)]
    public string Status { get; set; } = "Pending";

    public int Attempts { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset AvailableAt { get; set; }

    public DateTimeOffset? LockedUntil { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }

    public string? LastError { get; set; }
}
