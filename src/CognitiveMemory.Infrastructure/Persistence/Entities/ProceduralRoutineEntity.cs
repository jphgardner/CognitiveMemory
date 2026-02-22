namespace CognitiveMemory.Infrastructure.Persistence.Entities;

public sealed class ProceduralRoutineEntity
{
    public Guid RoutineId { get; set; }
    public Guid CompanionId { get; set; }
    public string Trigger { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string StepsJson { get; set; } = "[]";
    public string CheckpointsJson { get; set; } = "[]";
    public string Outcome { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
