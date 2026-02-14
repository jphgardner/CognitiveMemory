using CognitiveMemory.Application.Contracts;

namespace CognitiveMemory.Application.Services;

public partial class MemoryService
{
    private static string BuildQueryCacheKey(QueryClaimsRequest request)
    {
        var filter = request.Filters.Subject ?? string.Empty;
        return $"query:{request.Text.Trim().ToLowerInvariant()}:{filter.Trim().ToLowerInvariant()}:{request.TopK}:{request.IncludeEvidence}:{request.IncludeContradictions}";
    }

    private static string BuildRetrievalQueryText(string question, string? conversationHistory)
    {
        if (string.IsNullOrWhiteSpace(conversationHistory))
        {
            return question;
        }

        var history = TrimContext(conversationHistory, MaxConversationContextChars);
        return $"{question}\n\nConversation context:\n{history}";
    }

    private static string BuildDebateQuestionText(string question, string? conversationHistory)
    {
        if (string.IsNullOrWhiteSpace(conversationHistory))
        {
            return question;
        }

        var history = TrimContext(conversationHistory, MaxConversationContextChars);
        return $"Current user question:\n{question}\n\nPrior conversation:\n{history}";
    }

    private static string TrimContext(string text, int maxChars)
    {
        var trimmed = text.Trim();
        if (trimmed.Length <= maxChars)
        {
            return trimmed;
        }

        return trimmed[^maxChars..];
    }

    private static QueryClaimsResponse RehydrateCachedResponse(QueryClaimsResponse cached, string requestId)
    {
        return new QueryClaimsResponse
        {
            Claims = cached.Claims,
            Meta = new QueryMeta
            {
                Strategy = "hybrid+cache",
                LatencyMs = 0,
                RequestId = requestId,
                UncertaintyFlags = cached.Meta.UncertaintyFlags
            }
        };
    }

}
