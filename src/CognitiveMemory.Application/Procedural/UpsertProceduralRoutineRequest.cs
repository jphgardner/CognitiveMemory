namespace CognitiveMemory.Application.Procedural;

public sealed record UpsertProceduralRoutineRequest(
    Guid? RoutineId,
    string Trigger,
    string Name,
    IReadOnlyList<string> Steps,
    IReadOnlyList<string> Checkpoints,
    string Outcome);
