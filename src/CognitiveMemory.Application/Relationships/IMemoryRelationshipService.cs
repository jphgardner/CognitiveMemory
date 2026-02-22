using CognitiveMemory.Domain.Memory;
using CognitiveMemory.Application.Abstractions;

namespace CognitiveMemory.Application.Relationships;

public interface IMemoryRelationshipService
{
    Task<MemoryRelationship> UpsertAsync(UpsertMemoryRelationshipRequest request, CancellationToken cancellationToken = default);
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
    Task<MemoryRelationshipBackfillResult> BackfillAsync(string? sessionId = null, int take = 2000, CancellationToken cancellationToken = default);
}
