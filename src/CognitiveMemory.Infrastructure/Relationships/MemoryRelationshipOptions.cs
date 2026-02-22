namespace CognitiveMemory.Infrastructure.Relationships;

public sealed class MemoryRelationshipOptions
{
    public bool Enabled { get; set; } = true;
    public bool AllowUnknownTypes { get; set; } = true;
    public double DefaultMinConfidence { get; set; } = 0.55;
    public double DefaultMinStrength { get; set; } = 0.5;
    public double DefaultConfidence { get; set; } = 0.7;
    public double DefaultStrength { get; set; } = 0.65;
    public List<RelationshipTypePolicyOptions> TypePolicies { get; set; } = [];
}

public sealed class RelationshipTypePolicyOptions
{
    public string RelationshipType { get; set; } = string.Empty;
    public double MinConfidence { get; set; } = 0.55;
    public double MinStrength { get; set; } = 0.5;
    public double DefaultConfidence { get; set; } = 0.7;
    public double DefaultStrength { get; set; } = 0.65;
}
