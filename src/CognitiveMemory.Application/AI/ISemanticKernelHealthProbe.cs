namespace CognitiveMemory.Application.AI;

public interface ISemanticKernelHealthProbe
{
    SemanticKernelHealthStatus GetStatus();
}
