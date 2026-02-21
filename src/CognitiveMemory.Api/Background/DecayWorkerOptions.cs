namespace CognitiveMemory.Api.Background;

public sealed class DecayWorkerOptions
{
    public bool Enabled { get; set; } = true;
    public int IntervalMinutes { get; set; } = 60;
    public int StaleDays { get; set; } = 30;
    public double DecayStep { get; set; } = 0.05;
    public double MinConfidence { get; set; } = 0.2;
}
