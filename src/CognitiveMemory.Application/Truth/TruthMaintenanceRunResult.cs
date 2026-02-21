namespace CognitiveMemory.Application.Truth;

public sealed record TruthMaintenanceRunResult(
    int ClaimsScanned,
    int ConflictClusters,
    int ContradictionsRecorded,
    int ConfidenceAdjustments,
    int ProbabilisticMarks,
    IReadOnlyList<string> ClarificationRequests,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc);
