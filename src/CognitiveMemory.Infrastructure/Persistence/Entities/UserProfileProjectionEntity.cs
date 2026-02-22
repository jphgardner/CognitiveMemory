namespace CognitiveMemory.Infrastructure.Persistence.Entities;

public sealed class UserProfileProjectionEntity
{
    public Guid CompanionId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
