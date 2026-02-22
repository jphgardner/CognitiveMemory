namespace CognitiveMemory.Application.Relationships;

public interface IMemoryRelationshipExtractionService
{
    Task<MemoryRelationshipExtractionResult> ExtractAsync(
        string sessionId,
        int take = 200,
        bool apply = true,
        CancellationToken cancellationToken = default);
}

public sealed record MemoryRelationshipExtractionResult(
    string SessionId,
    int CandidatesScanned,
    int ProposedRelationships,
    int AppliedRelationships,
    int RejectedRelationships,
    bool Applied,
    string? Notes = null);
