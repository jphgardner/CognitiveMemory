using CognitiveMemory.Application.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel.Embeddings;

namespace CognitiveMemory.Infrastructure.SemanticKernel;

public sealed class SemanticKernelEmbeddingProvider(
    IMemoryKernelFactory kernelFactory,
    IOptions<SemanticKernelOptions> options,
    ILogger<SemanticKernelEmbeddingProvider> logger) : ITextEmbeddingProvider
{
    public async Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken)
    {
        var kernel = kernelFactory.CreateKernel();
        var service = kernel.Services.GetRequiredService<ITextEmbeddingGenerationService>();

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, options.Value.EmbeddingTimeoutSeconds)));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        IList<ReadOnlyMemory<float>> embeddings;
        try
        {
            embeddings = await service.GenerateEmbeddingsAsync([text], kernel: kernel, cancellationToken: linkedCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            logger.LogWarning("Embedding generation timed out after {TimeoutSeconds}s.", options.Value.EmbeddingTimeoutSeconds);
            return ReadOnlyMemory<float>.Empty;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Embedding generation failed. Falling back to lexical retrieval only.");
            return ReadOnlyMemory<float>.Empty;
        }

        if (embeddings.Count == 0)
        {
            return ReadOnlyMemory<float>.Empty;
        }

        return embeddings[0];
    }
}
