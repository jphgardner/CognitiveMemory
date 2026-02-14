using CognitiveMemory.Application.AI.Tooling;
using Microsoft.SemanticKernel;

namespace CognitiveMemory.Application.AI.Plugins;

public sealed class DebateRolePlugin
{
    [KernelFunction("soften_confident_language")]
    public string SoftenConfidentLanguage(string input)
    {
        string softened;
        if (string.IsNullOrWhiteSpace(input))
        {
            softened = "Based on available evidence, the answer is uncertain.";
        }
        else
        {
            const string prefix = "Based on available evidence, it appears that ";
            softened = input.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? input
                : prefix + char.ToLowerInvariant(input[0]) + input[1..];
        }

        return ToolEnvelopeJson.Success(
            data: softened,
            code: "ok",
            message: "Confidence language softened.",
            traceId: ToolEnvelopeJson.ResolveTraceId());
    }
}
