using System.Security.Cryptography;
using System.Text;

namespace CognitiveMemory.Application.AI.Tooling;

public static class OutboxEventTypes
{
    public const string MemoryDocumentIngested = "memory.document.ingested";
    public const string MemoryClaimCreated = "memory.claim.created";
    public const string MemoryClaimSuperseded = "memory.claim.superseded";
    public const string MemoryClaimRetracted = "memory.claim.retracted";
    public const string MemoryContradictionFlagged = "memory.contradiction.flagged";
    public const string MemoryClaimEnriched = "memory.claim.enriched";
    public const string MemoryClaimCalibrationRecorded = "memory.claim.calibration.recorded";
    public const string MemoryClaimConfidenceUpdated = "memory.claim.confidence.updated";
    public const string MemoryAnswerGenerated = "memory.answer.generated";
    public const string ConscienceAnalysisCompleted = "conscience.analysis.completed";
}

public static class OutboxAggregateTypes
{
    public const string Document = "Document";
    public const string Claim = "Claim";
    public const string Contradiction = "Contradiction";
    public const string Answer = "Answer";
}

public static class OutboxStatuses
{
    public const string Pending = "Pending";
    public const string Failed = "Failed";
    public const string Processing = "Processing";
    public const string Succeeded = "Succeeded";
}

public static class PolicyDecisionSources
{
    public const string ChatAnswer = "ChatAnswer";
    public const string ConscienceWorker = "ConscienceWorker";
}

public static class IdempotencyKeyFactory
{
    public static string Resolve(string scope, string? provided, params string[] values)
    {
        if (!string.IsNullOrWhiteSpace(provided))
        {
            return provided.Trim();
        }

        var canonical = string.Join("|", values.Select(v => v?.Trim() ?? string.Empty));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{scope}|{canonical}")));
        return $"{scope}:{hash[..24]}";
    }
}
