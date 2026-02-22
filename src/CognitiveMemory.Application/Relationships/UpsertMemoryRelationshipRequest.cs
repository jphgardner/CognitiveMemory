using CognitiveMemory.Domain.Memory;

namespace CognitiveMemory.Application.Relationships;

public sealed record UpsertMemoryRelationshipRequest(
    string SessionId,
    MemoryNodeType FromType,
    string FromId,
    MemoryNodeType ToType,
    string ToId,
    string RelationshipType,
    double Confidence = 0.7,
    double Strength = 0.7,
    DateTimeOffset? ValidFromUtc = null,
    DateTimeOffset? ValidToUtc = null,
    string? MetadataJson = null);
