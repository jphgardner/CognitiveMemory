using System.Collections.Generic;
using System.Linq;
using CognitiveMemory.Application.AI.Tooling;
using CognitiveMemory.Application.Contracts;

namespace CognitiveMemory.Application.Services;

public partial class MemoryService
{
    private async Task<double> ComputeRetrievalRelevance(
        string queryText,
        string candidateText,
        ReadOnlyMemory<float> queryEmbedding,
        CancellationToken cancellationToken)
    {
        var queryTokens = Tokenize(queryText);
        var candidateTokens = Tokenize(candidateText);
        var lexical = ComputeJaccard(queryTokens, candidateTokens);

        var candidateEmbedding = await embeddingProvider.GenerateEmbeddingAsync(candidateText, cancellationToken);
        var semantic = CosineSimilarity(queryEmbedding.Span, candidateEmbedding.Span);

        return Math.Clamp((0.45 * lexical) + (0.55 * semantic), 0, 1);
    }

    private static string BuildCandidateRetrievalText(QueryCandidate candidate, ClaimInsightRecord? insight)
    {
        var baseText = $"{candidate.Predicate} {candidate.LiteralValue} {candidate.Scope}";
        if (insight is null)
        {
            return baseText;
        }

        var keywords = string.Join(' ', insight.Keywords);
        return $"{baseText} {insight.Summary} {keywords}";
    }

    private static HashSet<string> Tokenize(string text)
    {
        return text
            .ToLowerInvariant()
            .Split([' ', '.', ',', ':', ';', '/', '-', '_', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet();
    }

    private static double ComputeJaccard(HashSet<string> left, HashSet<string> right)
    {
        if (left.Count == 0 && right.Count == 0)
        {
            return 0;
        }

        var intersectionCount = left.Intersect(right).Count();
        var unionCount = left.Union(right).Count();
        return unionCount == 0 ? 0 : (double)intersectionCount / unionCount;
    }

    private static double CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length == 0 || b.Length == 0 || a.Length != b.Length)
        {
            return 0;
        }

        double dot = 0;
        double magA = 0;
        double magB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }

        if (magA == 0 || magB == 0)
        {
            return 0;
        }

        return Math.Clamp(dot / (Math.Sqrt(magA) * Math.Sqrt(magB)), 0, 1);
    }

}
