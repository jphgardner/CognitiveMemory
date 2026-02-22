namespace CognitiveMemory.Infrastructure.Persistence.Entities;

public sealed class SubconsciousDebateMetricEntity
{
    public Guid DebateId { get; set; }
    public Guid CompanionId { get; set; }
    public int TurnCount { get; set; }
    public int DurationMs { get; set; }
    public double ConvergenceScore { get; set; }
    public int ContradictionsDetected { get; set; }
    public int ClaimsProposed { get; set; }
    public int ClaimsApplied { get; set; }
    public bool RequiresUserInput { get; set; }
    public double FinalConfidence { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
