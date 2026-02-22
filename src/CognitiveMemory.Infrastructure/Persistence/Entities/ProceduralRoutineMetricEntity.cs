namespace CognitiveMemory.Infrastructure.Persistence.Entities;

public sealed class ProceduralRoutineMetricEntity
{
    public Guid RoutineId { get; set; }
    public Guid CompanionId { get; set; }
    public string Trigger { get; set; } = string.Empty;
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public string? LastOutcomeSummary { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
