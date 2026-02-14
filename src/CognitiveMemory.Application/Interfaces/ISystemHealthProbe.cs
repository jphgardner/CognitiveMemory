using CognitiveMemory.Application.Contracts;

namespace CognitiveMemory.Application.Interfaces;

public interface ISystemHealthProbe
{
    Task<MemoryHealthResponse> CheckAsync(CancellationToken cancellationToken);
}
