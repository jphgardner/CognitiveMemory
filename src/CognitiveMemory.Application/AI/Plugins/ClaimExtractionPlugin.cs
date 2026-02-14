using CognitiveMemory.Application.AI.Tooling;
using Microsoft.SemanticKernel;

namespace CognitiveMemory.Application.AI.Plugins;

public sealed class ClaimExtractionPlugin
{
    [KernelFunction("normalize_text")]
    public string NormalizeText(string input)
    {
        var normalized = input?.Trim() ?? string.Empty;
        return ToolEnvelopeJson.Success(
            data: normalized,
            code: "ok",
            message: "Text normalized.",
            traceId: ToolEnvelopeJson.ResolveTraceId());
    }
}
