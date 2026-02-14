using System.Collections.Generic;
using System.Linq;
using CognitiveMemory.Application.AI.Tooling;
using CognitiveMemory.Application.Contracts;

namespace CognitiveMemory.Application.Services;

public partial class MemoryService
{
    private static void ValidateClaimInvariant(CreateClaimRequest request)
    {
        var hasObject = request.ObjectEntityId.HasValue;
        var hasLiteral = !string.IsNullOrWhiteSpace(request.LiteralValue);

        if (hasObject == hasLiteral)
        {
            throw new ArgumentException("Exactly one of objectEntityId or literalValue must be provided.", nameof(request));
        }

        if (request.Evidence.Count == 0)
        {
            throw new ArgumentException("At least one evidence record is required.", nameof(request));
        }
    }

    private static DebateResult BuildAnswerFallback(QueryClaimsResponse queryResponse)
    {
        var top = queryResponse.Claims.FirstOrDefault();
        if (top is null)
        {
            return new DebateResult
            {
                Answer = "I do not have enough evidence to answer this confidently.",
                Confidence = 0,
                Citations = [],
                UncertaintyFlags = [ConscienceReasonCodes.InsufficientEvidence],
                Contradictions = []
            };
        }

        var citations = top.Evidence
            .Select(e => new AnswerCitation { ClaimId = top.ClaimId, EvidenceId = e.EvidenceId })
            .Take(2)
            .ToList();

        return new DebateResult
        {
            Answer = $"Based on available evidence, it appears that the best-supported value for '{top.Predicate}' is '{top.LiteralValue}'.",
            Confidence = Math.Clamp(top.Confidence, 0.1, 0.75),
            Citations = citations,
            UncertaintyFlags = queryResponse.Meta.UncertaintyFlags.Count == 0 ? [] : queryResponse.Meta.UncertaintyFlags,
            Contradictions = top.Contradictions
        };
    }

    private static AnswerConscience BuildConscienceDecision(DebateResult debated)
    {
        var reasonCodes = new List<string>();

        if (debated.Citations.Count == 0)
        {
            reasonCodes.Add(ConscienceReasonCodes.InsufficientEvidence);
        }

        if (debated.Contradictions.Any(c =>
                string.Equals(c.Status, "Open", StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(c.Severity, "High", StringComparison.OrdinalIgnoreCase) || string.Equals(c.Severity, "Critical", StringComparison.OrdinalIgnoreCase))))
        {
            reasonCodes.Add(ConscienceReasonCodes.SevereContradiction);
        }

        foreach (var flag in debated.UncertaintyFlags.Where(flag => !string.IsNullOrWhiteSpace(flag)))
        {
            if (!reasonCodes.Contains(flag, StringComparer.Ordinal))
            {
                reasonCodes.Add(flag);
            }
        }

        var decision = ConsciencePolicy.Approve;
        var risk = 0.1;
        if (reasonCodes.Contains(ConscienceReasonCodes.InsufficientEvidence, StringComparer.Ordinal))
        {
            decision = ConsciencePolicy.Block;
            risk = 0.9;
        }
        else if (reasonCodes.Contains(ConscienceReasonCodes.SevereContradiction, StringComparer.Ordinal))
        {
            decision = ConsciencePolicy.Revise;
            risk = 0.75;
        }
        else if (reasonCodes.Count > 0)
        {
            decision = ConsciencePolicy.Downgrade;
            risk = 0.45;
        }

        return new AnswerConscience
        {
            Decision = decision,
            RiskScore = risk,
            PolicyVersion = ConsciencePolicy.CurrentVersion,
            ReasonCodes = reasonCodes
        };
    }

}
