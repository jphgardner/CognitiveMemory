using CognitiveMemory.Application.Abstractions;
using CognitiveMemory.Domain.Memory;

namespace CognitiveMemory.Application.Relationships;

public sealed class MemoryRelationshipService(IMemoryRelationshipRepository repository) : IMemoryRelationshipService
{
    public async Task<MemoryRelationship> UpsertAsync(UpsertMemoryRelationshipRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId))
        {
            throw new ArgumentException("SessionId is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.FromId) || string.IsNullOrWhiteSpace(request.ToId))
        {
            throw new ArgumentException("FromId and ToId are required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.RelationshipType))
        {
            throw new ArgumentException("RelationshipType is required.", nameof(request));
        }

        var now = DateTimeOffset.UtcNow;
        var relationship = new MemoryRelationship(
            Guid.NewGuid(),
            request.SessionId.Trim(),
            request.FromType,
            request.FromId.Trim(),
            request.ToType,
            request.ToId.Trim(),
            request.RelationshipType.Trim().ToLowerInvariant(),
            Math.Clamp(request.Confidence, 0, 1),
            Math.Clamp(request.Strength, 0, 1),
            MemoryRelationshipStatus.Active,
            request.ValidFromUtc,
            request.ValidToUtc,
            request.MetadataJson,
            now,
            now);

        return await repository.UpsertAsync(relationship, cancellationToken);
    }

    public Task<bool> RetireAsync(Guid relationshipId, CancellationToken cancellationToken = default)
        => repository.RetireAsync(relationshipId, cancellationToken);

    public Task<IReadOnlyList<MemoryRelationship>> QueryBySessionAsync(
        string sessionId,
        string? relationshipType = null,
        MemoryRelationshipStatus? status = null,
        int take = 200,
        CancellationToken cancellationToken = default)
        => repository.QueryBySessionAsync(sessionId.Trim(), relationshipType?.Trim(), status, Math.Clamp(take, 1, 1000), cancellationToken);

    public Task<IReadOnlyList<MemoryRelationship>> QueryByNodeAsync(
        string sessionId,
        MemoryNodeType nodeType,
        string nodeId,
        string? relationshipType = null,
        int take = 200,
        CancellationToken cancellationToken = default)
        => repository.QueryByNodeAsync(
            sessionId.Trim(),
            nodeType,
            nodeId.Trim(),
            relationshipType?.Trim(),
            Math.Clamp(take, 1, 1000),
            cancellationToken);

    public Task<MemoryRelationshipBackfillResult> BackfillAsync(string? sessionId = null, int take = 2000, CancellationToken cancellationToken = default)
        => repository.BackfillAsync(sessionId?.Trim(), Math.Clamp(take, 100, 10000), cancellationToken);
}
