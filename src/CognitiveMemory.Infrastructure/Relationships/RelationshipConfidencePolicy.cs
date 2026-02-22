namespace CognitiveMemory.Infrastructure.Relationships;

public sealed class RelationshipConfidencePolicy(MemoryRelationshipOptions options) : IRelationshipConfidencePolicy
{
    private readonly Dictionary<string, RelationshipTypePolicyOptions> byType = options.TypePolicies
        .Where(x => !string.IsNullOrWhiteSpace(x.RelationshipType))
        .GroupBy(x => Normalize(x.RelationshipType))
        .ToDictionary(x => x.Key, x => x.Last(), StringComparer.Ordinal);

    public string NormalizeType(string relationshipType) => Normalize(relationshipType);

    public RelationshipPolicyResolution Resolve(string relationshipType, double? confidence, double? strength)
    {
        var normalized = Normalize(relationshipType);
        var known = byType.TryGetValue(normalized, out var typePolicy);
        if (!known && !options.AllowUnknownTypes)
        {
            return new RelationshipPolicyResolution(
                normalized,
                0,
                0,
                1,
                1,
                Accepted: false,
                IsKnownType: false);
        }

        var minConfidence = known ? typePolicy!.MinConfidence : options.DefaultMinConfidence;
        var minStrength = known ? typePolicy!.MinStrength : options.DefaultMinStrength;
        var defaultConfidence = known ? typePolicy!.DefaultConfidence : options.DefaultConfidence;
        var defaultStrength = known ? typePolicy!.DefaultStrength : options.DefaultStrength;

        var resolvedConfidence = Math.Clamp(confidence ?? defaultConfidence, 0, 1);
        var resolvedStrength = Math.Clamp(strength ?? defaultStrength, 0, 1);
        var accepted = resolvedConfidence >= Math.Clamp(minConfidence, 0, 1)
                       && resolvedStrength >= Math.Clamp(minStrength, 0, 1);

        return new RelationshipPolicyResolution(
            normalized,
            resolvedConfidence,
            resolvedStrength,
            Math.Clamp(minConfidence, 0, 1),
            Math.Clamp(minStrength, 0, 1),
            accepted,
            known);
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var cleaned = new string(
            value
                .Trim()
                .ToLowerInvariant()
                .Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-')
                .ToArray());
        return cleaned.Replace('-', '_');
    }
}
