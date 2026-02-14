using System.Text.Json;
using CognitiveMemory.Application.AI.Tooling;
using CognitiveMemory.Application.Contracts;
using Microsoft.SemanticKernel;

namespace CognitiveMemory.Application.AI.Plugins;

public sealed class GroundingPlugin
{
    [KernelFunction("require_citations")]
    public string RequireCitations(string draftAnswer, string citationsJson)
    {
        var traceId = ToolEnvelopeJson.ResolveTraceId();

        try
        {
            var citations = ParseCitations(citationsJson);
            var hasAnswer = !string.IsNullOrWhiteSpace(draftAnswer);
            var hasCitations = citations.Count > 0;

            var data = new
            {
                pass = hasAnswer && hasCitations,
                hasAnswer,
                citationCount = citations.Count
            };

            return ToolEnvelopeJson.Success(
                data: data,
                code: hasAnswer && hasCitations ? "grounded" : "grounding_failed",
                message: hasAnswer && hasCitations
                    ? "Grounding checks passed."
                    : "Missing answer text or citations.",
                traceId: traceId);
        }
        catch (Exception ex)
        {
            return ToolEnvelopeJson.Failure(
                code: "grounding_error",
                message: ex.Message,
                traceId: traceId);
        }
    }

    private static IReadOnlyList<AnswerCitation> ParseCitations(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<AnswerCitation>>(json);
            return parsed ?? [];
        }
        catch
        {
            return [];
        }
    }
}
