namespace CognitiveMemory.Infrastructure.Subconscious;

public sealed class SubconsciousDebateOptions
{
    public bool Enabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 2;
    public int MaxConcurrentDebates { get; set; } = 2;
    public int MaxDebateTurns { get; set; } = 8;
    public int DebateDebounceSeconds { get; set; } = 10;
    public int WorkingContextTake { get; set; } = 20;
    public int WorkingMemoryStaleMinutes { get; set; } = 30;
    public double ConvergenceDeltaMin { get; set; } = 0.02;
    public double TerminateConfidenceThreshold { get; set; } = 0.78;
    public bool ApplyOutcome { get; set; } = true;
    public int DeferredFollowUpDelayMinutes { get; set; } = 10;
    public bool AutoApproveHighConfidenceRequiringUserInput { get; set; } = true;
    public double AutoApproveConfidenceThreshold { get; set; } = 0.93;
    public bool RequireHumanApprovalForProtectedIdentity { get; set; } = true;
    public bool AllowAutomaticProtectedIdentityDowngrade { get; set; } = true;
    public string[] ProtectedIdentityDowngradeMarkers { get; set; } =
    [
        "unverified",
        "user_asserted",
        "uncertain",
        "unknown",
        "persona",
        "non_enforced",
        "reclassify"
    ];
    public string[] ProtectedIdentityKeys { get; set; } =
    [
        "identity.name",
        "identity.birth_datetime",
        "identity.origin",
        "identity.role"
    ];
}
