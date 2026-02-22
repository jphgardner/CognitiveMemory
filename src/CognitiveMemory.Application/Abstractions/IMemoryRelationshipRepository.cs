using CognitiveMemory.Domain.Memory;

namespace CognitiveMemory.Application.Abstractions;

public interface IMemoryRelationshipRepository
{
    Task<MemoryRelationship> UpsertAsync(MemoryRelationship relationship, CancellationToken cancellationToken = default);
    Task<bool> RetireAsync(Guid relationshipId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MemoryRelationship>> QueryBySessionAsync(
        string sessionId,
        string? relationshipType = null,
        MemoryRelationshipStatus? status = null,
        int take = 200,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MemoryRelationship>> QueryByNodeAsync(
        string sessionId,
        MemoryNodeType nodeType,
        string nodeId,
        string? relationshipType = null,
        int take = 200,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<Guid, int>> GetSemanticRelationshipDegreeAsync(
        string sessionId,
        IReadOnlyList<Guid> semanticClaimIds,
        CancellationToken cancellationToken = default);
    Task<MemoryRelationshipBackfillResult> BackfillAsync(string? sessionId = null, int take = 2000, CancellationToken cancellationToken = default);
}

public sealed record MemoryRelationshipBackfillResult(
    int ScannedClaims,
    int CreatedRelationships,
    int UpdatedRelationships);
