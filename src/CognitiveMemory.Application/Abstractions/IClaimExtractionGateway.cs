namespace CognitiveMemory.Application.Abstractions;

public interface IClaimExtractionGateway
{
    Task<ExtractedClaimCandidate?> ExtractAsync(string text, CancellationToken cancellationToken = default);
}

public sealed record ExtractedClaimCandidate(string Subject, string Predicate, string Value, double Confidence);
