namespace CognitiveMemory.Infrastructure.Persistence.Entities;

public sealed class ClaimContradictionEntity
{
    public Guid ContradictionId { get; set; }
    public Guid ClaimAId { get; set; }
    public Guid ClaimBId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public DateTimeOffset DetectedAtUtc { get; set; }
    public string Status { get; set; } = string.Empty;
}
