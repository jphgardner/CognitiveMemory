namespace CognitiveMemory.Application.AI;

public interface IClaimExtractionEngine
{
    Task<string> NormalizeAsync(string content, CancellationToken cancellationToken);

    Task<IReadOnlyList<ExtractedClaim>> ExtractAsync(
        string normalizedContent,
        ClaimExtractionContext? context,
        CancellationToken cancellationToken);
}
