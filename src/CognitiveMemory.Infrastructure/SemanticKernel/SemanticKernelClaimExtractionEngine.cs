using System.Text.Json;
using CognitiveMemory.Application.AI;
using CognitiveMemory.Application.AI.Plugins;
using CognitiveMemory.Application.AI.Tooling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;

namespace CognitiveMemory.Infrastructure.SemanticKernel;

public sealed class SemanticKernelClaimExtractionEngine(
    IMemoryKernelFactory kernelFactory,
    ClaimExtractionPlugin plugin,
    IOptions<SemanticKernelOptions> options,
    ILogger<SemanticKernelClaimExtractionEngine> logger) : IClaimExtractionEngine
{
    public async Task<string> NormalizeAsync(string content, CancellationToken cancellationToken)
    {
        var kernel = kernelFactory.CreateKernel();
        kernel.Plugins.AddFromObject(plugin, "claimExtraction");

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, options.Value.ClaimExtractionTimeoutSeconds)));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            var result = await kernel.InvokeAsync(
                "claimExtraction",
                "normalize_text",
                new KernelArguments { ["input"] = content },
                linkedCts.Token);

            var raw = result.GetValue<string>() ?? string.Empty;
            if (ToolEnvelopeJson.TryReadDataString(raw, out var normalized))
            {
                return normalized.Trim();
            }

            return string.IsNullOrWhiteSpace(raw) ? content.Trim() : raw.Trim();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            logger.LogWarning("Claim normalization timed out after {TimeoutSeconds}s. Using raw content.", options.Value.ClaimExtractionTimeoutSeconds);
            return content.Trim();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Claim normalization failed. Using raw content.");
            return content.Trim();
        }
    }

    public async Task<IReadOnlyList<ExtractedClaim>> ExtractAsync(
        string normalizedContent,
        ClaimExtractionContext? context,
        CancellationToken cancellationToken)
    {
        var kernel = kernelFactory.CreateKernel();
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, options.Value.ClaimExtractionTimeoutSeconds)));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            var prompt = await PromptLoader.LoadTextAsync(PromptCatalog.ClaimExtractionPromptPath, cancellationToken);
            var function = kernel.CreateFunctionFromPrompt(prompt, functionName: PromptCatalog.ClaimExtraction);
            var contextJson = JsonSerializer.Serialize(context ?? new ClaimExtractionContext());
            var result = await kernel.InvokeAsync(function, new KernelArguments
            {
                ["input"] = normalizedContent,
                ["context"] = contextJson
            }, linkedCts.Token);
            var raw = result.GetValue<string>() ?? string.Empty;
            var parsed = ParseClaims(raw);
            if (parsed.Count > 0)
            {
                return parsed;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            logger.LogWarning("Claim extraction timed out after {TimeoutSeconds}s; using fallback extraction.", options.Value.ClaimExtractionTimeoutSeconds);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SK extraction failed; using fallback extraction.");
        }

        return
        [
            new ExtractedClaim
            {
                SubjectKey = ResolveMetadataValue(context, "actorKey"),
                SubjectType = ResolveMetadataValue(context, "actorRole"),
                Predicate = "statement",
                LiteralValue = normalizedContent.Length > 256 ? normalizedContent[..256] : normalizedContent,
                Confidence = 0.4,
                EvidenceSummary = normalizedContent.Length > 256 ? normalizedContent[..256] : normalizedContent
            }
        ];
    }

    private static List<ExtractedClaim> ParseClaims(string raw)
    {
        try
        {
            using var document = JsonDocument.Parse(raw);
            if (!document.RootElement.TryGetProperty("claims", out var claimsElement) || claimsElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var claims = new List<ExtractedClaim>();
            foreach (var claim in claimsElement.EnumerateArray())
            {
                if (!claim.TryGetProperty("predicate", out var predicateEl) || predicateEl.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                if (!claim.TryGetProperty("evidenceSummary", out var evidenceEl) || evidenceEl.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var confidence = 0.5;
                if (claim.TryGetProperty("confidence", out var confidenceEl) && confidenceEl.TryGetDouble(out var conf))
                {
                    confidence = Math.Clamp(conf, 0.0, 1.0);
                }

                string? subjectKey = null;
                if (claim.TryGetProperty("subjectKey", out var subjectKeyEl) && subjectKeyEl.ValueKind == JsonValueKind.String)
                {
                    subjectKey = subjectKeyEl.GetString();
                }

                string? subjectName = null;
                if (claim.TryGetProperty("subjectName", out var subjectNameEl) && subjectNameEl.ValueKind == JsonValueKind.String)
                {
                    subjectName = subjectNameEl.GetString();
                }

                string? subjectType = null;
                if (claim.TryGetProperty("subjectType", out var subjectTypeEl) && subjectTypeEl.ValueKind == JsonValueKind.String)
                {
                    subjectType = subjectTypeEl.GetString();
                }

                string? literalValue = null;
                if (claim.TryGetProperty("literalValue", out var literalEl) && literalEl.ValueKind == JsonValueKind.String)
                {
                    literalValue = literalEl.GetString();
                }

                claims.Add(new ExtractedClaim
                {
                    SubjectKey = subjectKey,
                    SubjectName = subjectName,
                    SubjectType = subjectType,
                    Predicate = predicateEl.GetString()!,
                    LiteralValue = literalValue,
                    Confidence = confidence,
                    EvidenceSummary = evidenceEl.GetString()!
                });
            }

            return claims;
        }
        catch
        {
            return [];
        }
    }

    private static string? ResolveMetadataValue(ClaimExtractionContext? context, string key)
    {
        if (context?.Metadata is null)
        {
            return null;
        }

        return context.Metadata.TryGetValue(key, out var value) ? value : null;
    }
}
