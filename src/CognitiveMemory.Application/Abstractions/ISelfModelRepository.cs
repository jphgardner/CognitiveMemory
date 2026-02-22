using CognitiveMemory.Domain.Memory;

namespace CognitiveMemory.Application.Abstractions;

public interface ISelfModelRepository
{
    Task<SelfModelSnapshot> GetAsync(CancellationToken cancellationToken = default);
    Task SetPreferenceAsync(string key, string value, CancellationToken cancellationToken = default);
    Task<SelfModelSnapshot> GetAsync(Guid companionId, CancellationToken cancellationToken = default)
        => GetAsync(cancellationToken);
    Task SetPreferenceAsync(Guid companionId, string key, string value, CancellationToken cancellationToken = default)
        => SetPreferenceAsync(key, value, cancellationToken);
}
