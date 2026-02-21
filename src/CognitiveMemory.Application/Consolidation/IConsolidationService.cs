namespace CognitiveMemory.Application.Consolidation;

public interface IConsolidationService
{
    Task<ConsolidationRunResult> RunOnceAsync(CancellationToken cancellationToken = default);
}
