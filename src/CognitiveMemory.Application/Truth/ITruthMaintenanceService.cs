namespace CognitiveMemory.Application.Truth;

public interface ITruthMaintenanceService
{
    Task<TruthMaintenanceRunResult> RunOnceAsync(CancellationToken cancellationToken = default);
}
