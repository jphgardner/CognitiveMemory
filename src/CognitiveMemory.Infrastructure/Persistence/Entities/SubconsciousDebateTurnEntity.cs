namespace CognitiveMemory.Infrastructure.Persistence.Entities;

public sealed class SubconsciousDebateTurnEntity
{
    public Guid TurnId { get; set; }
    public Guid DebateId { get; set; }
    public Guid CompanionId { get; set; }
    public int TurnNumber { get; set; }
    public string AgentName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? StructuredPayloadJson { get; set; }
    public double? Confidence { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
