using CognitiveMemory.Domain.Memory;

namespace CognitiveMemory.Application.SelfModel;

public interface ISelfModelService
{
    Task<SelfModelSnapshot> GetAsync(CancellationToken cancellationToken = default);
    Task SetPreferenceAsync(string key, string value, CancellationToken cancellationToken = default);
}
