namespace CognitiveMemory.Application.Identity;

public sealed record IdentityEvolutionRunResult(
    int EpisodesScanned,
    int ClaimsScanned,
    int ProceduresScanned,
    int PreferencesUpdated,
    IReadOnlyList<string> UpdatedKeys,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc);
