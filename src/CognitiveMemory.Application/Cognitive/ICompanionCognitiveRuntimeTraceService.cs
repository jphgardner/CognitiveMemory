namespace CognitiveMemory.Application.Cognitive;

public interface ICompanionCognitiveRuntimeTraceService
{
    Task WriteAsync(
        Guid companionId,
        string sessionId,
        Guid profileVersionId,
        string requestCorrelationId,
        string phase,
        string decisionJson,
        int latencyMs,
        CancellationToken cancellationToken = default);
}
