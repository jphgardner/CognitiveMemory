namespace CognitiveMemory.Domain.Memory;

public sealed record ProceduralRoutine(
    Guid RoutineId,
    string Trigger,
    string Name,
    IReadOnlyList<string> Steps,
    IReadOnlyList<string> Checkpoints,
    string Outcome,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
