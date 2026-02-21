using System.Text.Json;
using CognitiveMemory.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace CognitiveMemory.Infrastructure.SemanticKernel;

public sealed class SemanticKernelClaimExtractionGateway(
    ClaimExtractionKernel claimExtractionKernel,
    ILogger<SemanticKernelClaimExtractionGateway> logger) : IClaimExtractionGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ExtractedClaimCandidate?> ExtractAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var prompt =
            "Extract at most one factual claim from the input." + Environment.NewLine +
            "Use only the given input text; do not assume hidden memory." + Environment.NewLine +
            "If no extractable factual claim exists, return exactly: {\"isClaim\":false}" + Environment.NewLine +
            "If a claim exists, return strict JSON exactly with keys: isClaim, subject, predicate, value, confidence" + Environment.NewLine +
            "Do not output markdown or code fences." + Environment.NewLine +
            "Confidence must be between 0 and 1." + Environment.NewLine +
            $"Input: {text}";

        string? raw;
        try
        {
            var result = await claimExtractionKernel.Value.InvokePromptAsync(prompt, cancellationToken: cancellationToken);
            raw = result.GetValue<string>();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Claim extraction model call failed. Falling back to heuristic extraction.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            var model = JsonSerializer.Deserialize<ClaimExtractionResult>(raw, JsonOptions);
            if (model is null || !model.IsClaim)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(model.Subject) || string.IsNullOrWhiteSpace(model.Predicate) || string.IsNullOrWhiteSpace(model.Value))
            {
                return null;
            }

            return new ExtractedClaimCandidate(
                model.Subject.Trim(),
                model.Predicate.Trim(),
                model.Value.Trim(),
                model.Confidence ?? 0.5);
        }
        catch
        {
            logger.LogDebug("Claim extraction model returned non-JSON output.");
            return null;
        }
    }

    private sealed class ClaimExtractionResult
    {
        public bool IsClaim { get; set; }
        public string? Subject { get; set; }
        public string? Predicate { get; set; }
        public string? Value { get; set; }
        public double? Confidence { get; set; }
    }
}
