namespace CognitiveMemory.Infrastructure.Relationships;

public interface IRelationshipConfidencePolicy
{
    string NormalizeType(string relationshipType);
    RelationshipPolicyResolution Resolve(string relationshipType, double? confidence, double? strength);
}

public sealed record RelationshipPolicyResolution(
    string RelationshipType,
    double Confidence,
    double Strength,
    double MinConfidence,
    double MinStrength,
    bool Accepted,
    bool IsKnownType);
