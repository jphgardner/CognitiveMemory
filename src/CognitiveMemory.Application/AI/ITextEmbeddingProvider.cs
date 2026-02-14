namespace CognitiveMemory.Application.AI;

public interface ITextEmbeddingProvider
{
    Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken);
}
