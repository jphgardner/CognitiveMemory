namespace CognitiveMemory.Domain.Memory;

public sealed record SelfModelSnapshot(IReadOnlyList<SelfPreference> Preferences);
