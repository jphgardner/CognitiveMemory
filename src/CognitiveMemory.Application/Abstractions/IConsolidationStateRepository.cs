namespace CognitiveMemory.Application.Abstractions;

public interface IConsolidationStateRepository
{
    Task<bool> IsProcessedAsync(Guid episodicEventId, CancellationToken cancellationToken = default);
    Task MarkProcessedAsync(
        Guid episodicEventId,
        string outcome,
        Guid? semanticClaimId = null,
        string? notes = null,
        CancellationToken cancellationToken = default);
}
