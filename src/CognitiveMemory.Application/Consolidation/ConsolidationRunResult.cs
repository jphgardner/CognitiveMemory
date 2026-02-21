namespace CognitiveMemory.Application.Consolidation;

public sealed record ConsolidationRunResult(
    int Scanned,
    int Processed,
    int Promoted,
    int Skipped,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc);
