namespace CognitiveMemory.Application.Reasoning;

public sealed record CognitiveReasoningRunResult(
    int EpisodesScanned,
    int ClaimsScanned,
    int InferredClaims,
    int ConfidenceAdjustments,
    int WeakClaimsIdentified,
    int ProceduralSuggestions,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc);
