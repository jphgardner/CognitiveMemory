using System.Collections.Generic;
using System.Linq;
using CognitiveMemory.Application.AI.Tooling;
using CognitiveMemory.Application.Contracts;
using CognitiveMemory.Application.Interfaces;
using Microsoft.SemanticKernel;

namespace CognitiveMemory.Application.AI.Plugins;

public sealed class MemoryRecallPlugin(
    IClaimRepository claimRepository,
    AgentToolingGuard guard)
{
    [KernelFunction("search_claims_filtered")]
    public Task<string> SearchClaimsFilteredAsync(
        string query,
        int topK = 5,
        string? subjectFilter = null,
        string? predicateFilter = null,
        string? literalContains = null,
        string? sourceTypeFilter = null,
        double? minConfidence = null,
        double? minScore = null,
        string? scopeContains = null,
        CancellationToken cancellationToken = default)
    {
        return SearchClaimsCoreAsync(
            query: query,
            topK: topK,
            subjectFilter: subjectFilter,
            predicateFilter: predicateFilter,
            literalContains: literalContains,
            sourceTypeFilter: sourceTypeFilter,
            minConfidence: minConfidence,
            minScore: minScore,
            scopeContains: scopeContains,
            cancellationToken: cancellationToken);
    }

    [KernelFunction("search_claims")]
    public Task<string> SearchClaimsAsync(
        string query,
        int topK = 5,
        string? subjectFilter = null,
        CancellationToken cancellationToken = default)
    {
        return SearchClaimsCoreAsync(
            query: query,
            topK: topK,
            subjectFilter: subjectFilter,
            predicateFilter: null,
            literalContains: null,
            sourceTypeFilter: null,
            minConfidence: null,
            minScore: null,
            scopeContains: null,
            cancellationToken: cancellationToken);
    }

    private Task<string> SearchClaimsCoreAsync(
        string query,
        int topK,
        string? subjectFilter,
        string? predicateFilter,
        string? literalContains,
        string? sourceTypeFilter,
        double? minConfidence,
        double? minScore,
        string? scopeContains,
        CancellationToken cancellationToken)
    {
        var traceId = ToolEnvelopeJson.ResolveTraceId();

        return guard.RunAsync(async ct =>
        {
            try
            {
                var q = (query ?? string.Empty).ToLowerInvariant();
                var boundedTopK = Math.Clamp(topK, 1, 20);
                var candidateBudget = Math.Clamp(boundedTopK * 12, 24, 180);
                var normalizedPredicateFilter = NormalizeFilter(predicateFilter);
                var normalizedLiteralContains = NormalizeFilter(literalContains);
                var normalizedSourceTypeFilter = NormalizeFilter(sourceTypeFilter);
                var normalizedScopeContains = NormalizeFilter(scopeContains);
                var boundedMinConfidence = minConfidence.HasValue ? Math.Clamp(minConfidence.Value, 0, 1) : (double?)null;
                var boundedMinScore = minScore.HasValue ? Math.Clamp(minScore.Value, 0, 1) : (double?)null;
                var candidates = await claimRepository.GetQueryCandidatesAsync(subjectFilter, ct, candidateBudget);

                var scored = candidates
                    .Select(c => new
                    {
                        Candidate = c,
                        Score = LexicalScore(q, c)
                    })
                    .Where(x => MatchesFilters(
                        x.Candidate,
                        x.Score,
                        normalizedPredicateFilter,
                        normalizedLiteralContains,
                        normalizedSourceTypeFilter,
                        normalizedScopeContains,
                        boundedMinConfidence,
                        boundedMinScore))
                    .OrderByDescending(x => x.Score)
                    .ThenBy(x => x.Candidate.ClaimId)
                    .Take(boundedTopK)
                    .Select(x => new
                    {
                        claimId = x.Candidate.ClaimId,
                        predicate = x.Candidate.Predicate,
                        literalValue = x.Candidate.LiteralValue,
                        confidence = x.Candidate.Confidence,
                        score = x.Score,
                        evidenceCount = x.Candidate.Evidence.Count,
                        contradictionCount = x.Candidate.Contradictions.Count
                    })
                    .ToList();

                var filterSummary = BuildFilterSummary(
                    subjectFilter,
                    normalizedPredicateFilter,
                    normalizedLiteralContains,
                    normalizedSourceTypeFilter,
                    boundedMinConfidence,
                    boundedMinScore,
                    normalizedScopeContains);

                return ToolEnvelopeJson.Success(
                    data: scored,
                    code: "ok",
                    message: $"Returned {scored.Count} claims{filterSummary}.",
                    traceId: traceId);
            }
            catch (Exception ex)
            {
                return ToolEnvelopeJson.Failure(
                    code: "search_claims_failed",
                    message: ex.Message,
                    data: new
                    {
                        query,
                        subjectFilter,
                        predicateFilter,
                        literalContains,
                        sourceTypeFilter,
                        minConfidence,
                        minScore,
                        scopeContains
                    },
                    traceId: traceId);
            }
        }, cancellationToken);
    }

    [KernelFunction("get_claim")]
    public Task<string> GetClaimAsync(Guid claimId, CancellationToken cancellationToken = default)
    {
        var traceId = ToolEnvelopeJson.ResolveTraceId();

        return guard.RunAsync(async ct =>
        {
            try
            {
                var candidate = await claimRepository.GetQueryCandidateByIdAsync(claimId, ct);
                if (candidate is null)
                {
                    return ToolEnvelopeJson.Failure(
                        code: "not_found",
                        message: "Claim not found.",
                        data: new { claimId },
                        traceId: traceId);
                }

                return ToolEnvelopeJson.Success(
                    data: candidate,
                    code: "ok",
                    message: "Claim loaded.",
                    traceId: traceId);
            }
            catch (Exception ex)
            {
                return ToolEnvelopeJson.Failure(
                    code: "get_claim_failed",
                    message: ex.Message,
                    data: new { claimId },
                    traceId: traceId);
            }
        }, cancellationToken);
    }

    [KernelFunction("get_evidence")]
    public Task<string> GetEvidenceAsync(Guid claimId, CancellationToken cancellationToken = default)
    {
        var traceId = ToolEnvelopeJson.ResolveTraceId();

        return guard.RunAsync(async ct =>
        {
            try
            {
                var candidate = await claimRepository.GetQueryCandidateByIdAsync(claimId, ct);
                var result = (candidate?.Evidence.Select(e => new
                {
                    claimId,
                    evidenceId = e.EvidenceId,
                    sourceType = e.SourceType,
                    sourceRef = e.SourceRef,
                    strength = e.Strength
                }) ?? Enumerable.Empty<object>())
                    .ToList();

                return ToolEnvelopeJson.Success(
                    data: result,
                    code: "ok",
                    message: $"Returned {result.Count} evidence rows.",
                    traceId: traceId);
            }
            catch (Exception ex)
            {
                return ToolEnvelopeJson.Failure(
                    code: "get_evidence_failed",
                    message: ex.Message,
                    data: new { claimId },
                    traceId: traceId);
            }
        }, cancellationToken);
    }

    private static double LexicalScore(string query, QueryCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return candidate.Confidence;
        }

        var text = $"{candidate.Predicate} {candidate.LiteralValue} {candidate.Scope}".ToLowerInvariant();
        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var hitCount = tokens.Count(token => text.Contains(token, StringComparison.Ordinal));
        var lexical = tokens.Length == 0 ? 0 : (double)hitCount / tokens.Length;
        return Math.Clamp((0.65 * lexical) + (0.35 * candidate.Confidence), 0, 1);
    }

    private static bool MatchesFilters(
        QueryCandidate candidate,
        double score,
        string? predicateFilter,
        string? literalContains,
        string? sourceTypeFilter,
        string? scopeContains,
        double? minConfidence,
        double? minScore)
    {
        if (!string.IsNullOrWhiteSpace(predicateFilter) &&
            !candidate.Predicate.Contains(predicateFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(literalContains) &&
            !(candidate.LiteralValue ?? string.Empty).Contains(literalContains, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(sourceTypeFilter) &&
            !candidate.Evidence.Any(e => string.Equals(e.SourceType, sourceTypeFilter, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(scopeContains) &&
            !candidate.Scope.Contains(scopeContains, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (minConfidence.HasValue && candidate.Confidence < minConfidence.Value)
        {
            return false;
        }

        if (minScore.HasValue && score < minScore.Value)
        {
            return false;
        }

        return true;
    }

    private static string? NormalizeFilter(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string BuildFilterSummary(
        string? subjectFilter,
        string? predicateFilter,
        string? literalContains,
        string? sourceTypeFilter,
        double? minConfidence,
        double? minScore,
        string? scopeContains)
    {
        var filters = new List<string>();
        if (!string.IsNullOrWhiteSpace(subjectFilter)) filters.Add($"subject={subjectFilter}");
        if (!string.IsNullOrWhiteSpace(predicateFilter)) filters.Add($"predicate~={predicateFilter}");
        if (!string.IsNullOrWhiteSpace(literalContains)) filters.Add($"literal~={literalContains}");
        if (!string.IsNullOrWhiteSpace(sourceTypeFilter)) filters.Add($"sourceType={sourceTypeFilter}");
        if (minConfidence.HasValue) filters.Add($"minConfidence={minConfidence.Value:F2}");
        if (minScore.HasValue) filters.Add($"minScore={minScore.Value:F2}");
        if (!string.IsNullOrWhiteSpace(scopeContains)) filters.Add($"scope~={scopeContains}");

        return filters.Count == 0 ? string.Empty : $" (filters: {string.Join(", ", filters)})";
    }
}
