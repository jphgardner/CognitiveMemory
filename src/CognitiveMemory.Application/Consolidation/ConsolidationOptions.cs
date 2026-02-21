namespace CognitiveMemory.Application.Consolidation;

public sealed class ConsolidationOptions
{
    public int LookbackHours { get; set; } = 24;
    public int MaxCandidatesPerRun { get; set; } = 500;
    public double MinExtractionConfidence { get; set; } = 0.6;
    public int MinOccurrencesForPromotion { get; set; } = 1;
}
