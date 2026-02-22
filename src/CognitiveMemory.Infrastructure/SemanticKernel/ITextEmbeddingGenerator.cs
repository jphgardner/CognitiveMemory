namespace CognitiveMemory.Infrastructure.SemanticKernel;

public interface ITextEmbeddingGenerator
{
    Task<float[]?> GenerateAsync(string text, CancellationToken cancellationToken = default);
}
