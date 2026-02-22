namespace CognitiveMemory.Domain.Memory;

public sealed record MemoryRelationship(
    Guid RelationshipId,
    string SessionId,
    MemoryNodeType FromType,
    string FromId,
    MemoryNodeType ToType,
    string ToId,
    string RelationshipType,
    double Confidence,
    double Strength,
    MemoryRelationshipStatus Status,
    DateTimeOffset? ValidFromUtc,
    DateTimeOffset? ValidToUtc,
    string? MetadataJson,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
