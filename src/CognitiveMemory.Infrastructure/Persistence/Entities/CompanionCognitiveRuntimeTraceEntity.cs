namespace CognitiveMemory.Infrastructure.Persistence.Entities;

public sealed class CompanionCognitiveRuntimeTraceEntity
{
    public Guid TraceId { get; set; }
    public Guid CompanionId { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public Guid ProfileVersionId { get; set; }
    public string RequestCorrelationId { get; set; } = string.Empty;
    public string Phase { get; set; } = string.Empty;
    public string DecisionJson { get; set; } = "{}";
    public int LatencyMs { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
