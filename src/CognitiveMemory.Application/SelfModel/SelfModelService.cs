using CognitiveMemory.Application.Abstractions;
using CognitiveMemory.Domain.Memory;

namespace CognitiveMemory.Application.SelfModel;

public sealed class SelfModelService(ISelfModelRepository repository) : ISelfModelService
{
    public Task<SelfModelSnapshot> GetAsync(CancellationToken cancellationToken = default) => repository.GetAsync(cancellationToken);

    public Task SetPreferenceAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Key is required.", nameof(key));
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Value is required.", nameof(value));
        return repository.SetPreferenceAsync(key.Trim(), value.Trim(), cancellationToken);
    }
}
