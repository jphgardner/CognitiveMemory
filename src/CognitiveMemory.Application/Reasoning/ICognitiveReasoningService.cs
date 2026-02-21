namespace CognitiveMemory.Application.Reasoning;

public interface ICognitiveReasoningService
{
    Task<CognitiveReasoningRunResult> RunOnceAsync(CancellationToken cancellationToken = default);
}
