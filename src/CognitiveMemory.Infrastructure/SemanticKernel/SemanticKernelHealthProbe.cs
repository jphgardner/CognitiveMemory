using CognitiveMemory.Application.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Embeddings;

namespace CognitiveMemory.Infrastructure.SemanticKernel;

public sealed class SemanticKernelHealthProbe(
    IMemoryKernelFactory kernelFactory,
    IOptions<SemanticKernelOptions> options) : ISemanticKernelHealthProbe
{
    public SemanticKernelHealthStatus GetStatus()
    {
        try
        {
            var kernel = kernelFactory.CreateKernel();
            _ = kernel.Services.GetRequiredService<IChatCompletionService>();
            _ = kernel.Services.GetRequiredService<ITextEmbeddingGenerationService>();

            return new SemanticKernelHealthStatus
            {
                Provider = options.Value.Provider,
                ModelStatus = "ok"
            };
        }
        catch
        {
            return new SemanticKernelHealthStatus
            {
                Provider = options.Value.Provider,
                ModelStatus = "unavailable"
            };
        }
    }
}
