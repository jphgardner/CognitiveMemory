namespace CognitiveMemory.Api.Background;

public sealed class IdentityEvolutionWorkerOptions
{
    public bool Enabled { get; set; } = true;
    public int IntervalMinutes { get; set; } = 60;
}
