using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Embeddings;

namespace CognitiveMemory.Infrastructure.SemanticKernel;

public sealed class InMemoryTextEmbeddingGenerationService : ITextEmbeddingGenerationService
{
    public IReadOnlyDictionary<string, object?> Attributes => new Dictionary<string, object?>();

    public Task<IList<ReadOnlyMemory<float>>> GenerateEmbeddingsAsync(
        IList<string> data,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        IList<ReadOnlyMemory<float>> embeddings = data
            .Select(text =>
            {
                var lengthSignal = Math.Clamp(text.Length / 100f, 0f, 1f);
                var checksumSignal = (text.Sum(c => c) % 1000) / 1000f;
                return new ReadOnlyMemory<float>([lengthSignal, checksumSignal, 1f - lengthSignal, 1f]);
            })
            .ToList();

        return Task.FromResult(embeddings);
    }
}
