namespace CognitiveMemory.Infrastructure.SemanticKernel;

public sealed class SemanticKernelToolingOptions
{
    public bool EnableMemoryTools { get; set; } = true;
    public bool AutoInvokeTools { get; set; } = true;
    public string PluginName { get; set; } = "MemoryTools";
}
