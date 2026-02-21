namespace CognitiveMemory.Domain.Memory;

public sealed record SelfPreference(string Key, string Value, DateTimeOffset UpdatedAtUtc);
