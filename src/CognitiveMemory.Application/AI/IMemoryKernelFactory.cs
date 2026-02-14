using Microsoft.SemanticKernel;

namespace CognitiveMemory.Application.AI;

public interface IMemoryKernelFactory
{
    Kernel CreateKernel();
}
