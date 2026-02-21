using CognitiveMemory.Domain.Memory;

namespace CognitiveMemory.Application.Abstractions;

public interface ISelfModelRepository
{
    Task<SelfModelSnapshot> GetAsync(CancellationToken cancellationToken = default);
    Task SetPreferenceAsync(string key, string value, CancellationToken cancellationToken = default);
}
