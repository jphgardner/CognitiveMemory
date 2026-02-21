using Microsoft.SemanticKernel;

namespace CognitiveMemory.Infrastructure.SemanticKernel;

public sealed class ClaimExtractionKernel(Kernel value)
{
    public Kernel Value { get; } = value;
}
