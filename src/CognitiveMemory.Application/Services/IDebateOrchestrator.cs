using CognitiveMemory.Application.Contracts;

namespace CognitiveMemory.Application.Services;

public interface IDebateOrchestrator
{
    Task<DebateResult> OrchestrateAsync(string question, QueryClaimsResponse memoryPacket, CancellationToken cancellationToken);
}
