namespace CognitiveMemory.Infrastructure.Persistence.Entities;

public sealed class SelfPreferenceEntity
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
