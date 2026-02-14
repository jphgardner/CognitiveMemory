using System.Text.Json;
using CognitiveMemory.Application.AI.Plugins;
using Microsoft.SemanticKernel;

namespace CognitiveMemory.Api.Endpoints;

public static partial class OpenAiCompatEndpoints
{
    private sealed class ToolExecutionCollector
    {
        private readonly List<OpenAiToolExecution> _executions = [];
        private readonly object _gate = new();

        public void Record(string toolName, string responseJson, string source = "agent")
        {
            var execution = Parse(toolName, responseJson, source);
            lock (_gate)
            {
                _executions.Add(execution);
            }
        }

        public IReadOnlyList<OpenAiToolExecution> Snapshot()
        {
            lock (_gate)
            {
                return _executions.ToArray();
            }
        }

        private static OpenAiToolExecution Parse(string toolName, string responseJson, string source)
        {
            if (string.IsNullOrWhiteSpace(responseJson))
            {
                return new OpenAiToolExecution
                {
                    ToolName = toolName,
                    Source = source,
                    Ok = false,
                    Code = "empty_payload",
                    Message = "Tool returned an empty payload.",
                    ResultCount = 0
                };
            }

            try
            {
                using var document = JsonDocument.Parse(responseJson);
                var root = document.RootElement;
                var ok = false;
                if (root.TryGetProperty("ok", out var okElement) && okElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    ok = okElement.GetBoolean();
                }

                var eventIds = new List<Guid>();
                if (root.TryGetProperty("eventIds", out var eventIdsElement) && eventIdsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var eventIdElement in eventIdsElement.EnumerateArray())
                    {
                        if (eventIdElement.ValueKind == JsonValueKind.String && Guid.TryParse(eventIdElement.GetString(), out var eventId))
                        {
                            eventIds.Add(eventId);
                        }
                    }
                }

                JsonElement? data = null;
                if (root.TryGetProperty("data", out var dataElement))
                {
                    data = dataElement.Clone();
                }

                return new OpenAiToolExecution
                {
                    ToolName = toolName,
                    Source = source,
                    Ok = ok,
                    Code = root.TryGetProperty("code", out var codeElement) && codeElement.ValueKind == JsonValueKind.String
                        ? codeElement.GetString() ?? string.Empty
                        : string.Empty,
                    Message = root.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String
                        ? messageElement.GetString() ?? string.Empty
                        : string.Empty,
                    IdempotencyKey = root.TryGetProperty("idempotencyKey", out var idempotencyElement) && idempotencyElement.ValueKind == JsonValueKind.String
                        ? idempotencyElement.GetString() ?? string.Empty
                        : string.Empty,
                    TraceId = root.TryGetProperty("traceId", out var traceElement) && traceElement.ValueKind == JsonValueKind.String
                        ? traceElement.GetString() ?? string.Empty
                        : string.Empty,
                    Data = data,
                    EventIds = eventIds,
                    ResultCount = CountDataRecords(root)
                };
            }
            catch
            {
                return new OpenAiToolExecution
                {
                    ToolName = toolName,
                    Source = source,
                    Ok = false,
                    Code = "invalid_payload",
                    Message = "Tool returned non-JSON payload.",
                    ResultCount = 0
                };
            }
        }

        private static int CountDataRecords(JsonElement root)
        {
            if (!root.TryGetProperty("data", out var data))
            {
                return 0;
            }

            if (data.ValueKind == JsonValueKind.Array)
            {
                return data.GetArrayLength();
            }

            if (data.ValueKind == JsonValueKind.Object || data.ValueKind == JsonValueKind.String || data.ValueKind == JsonValueKind.Number || data.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return 1;
            }

            return 0;
        }
    }

    private sealed class TrackingMemoryRecallPlugin(MemoryRecallPlugin inner, ToolExecutionCollector collector)
    {
        public async Task<string> SearchClaimsForPrefetchAsync(
            string query,
            int topK = 5,
            string? subjectFilter = null,
            CancellationToken cancellationToken = default)
        {
            var response = await inner.SearchClaimsAsync(
                query: query,
                topK: topK,
                subjectFilter: subjectFilter,
                cancellationToken: cancellationToken);
            collector.Record("memory_recall.search_claims", response, source: "prefetch");
            return response;
        }

        public async Task<string> SearchClaimsFilteredForPrefetchAsync(
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
            var response = await inner.SearchClaimsFilteredAsync(
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
            collector.Record("memory_recall.search_claims_filtered", response, source: "prefetch");
            return response;
        }

        [KernelFunction("search_claims_filtered")]
        public async Task<string> SearchClaimsFilteredAsync(
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
            var response = await inner.SearchClaimsFilteredAsync(
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
            collector.Record("memory_recall.search_claims_filtered", response);
            return response;
        }

        public async Task<string> GetClaimForPrefetchAsync(Guid claimId, CancellationToken cancellationToken = default)
        {
            var response = await inner.GetClaimAsync(claimId, cancellationToken);
            collector.Record("memory_recall.get_claim", response, source: "prefetch");
            return response;
        }

        public async Task<string> GetEvidenceForPrefetchAsync(Guid claimId, CancellationToken cancellationToken = default)
        {
            var response = await inner.GetEvidenceAsync(claimId, cancellationToken);
            collector.Record("memory_recall.get_evidence", response, source: "prefetch");
            return response;
        }

        [KernelFunction("search_claims")]
        public async Task<string> SearchClaimsAsync(
            string query,
            int topK = 5,
            string? subjectFilter = null,
            CancellationToken cancellationToken = default)
        {
            var response = await inner.SearchClaimsAsync(
                query: query,
                topK: topK,
                subjectFilter: subjectFilter,
                cancellationToken: cancellationToken);
            collector.Record("memory_recall.search_claims", response);
            return response;
        }

        [KernelFunction("get_claim")]
        public async Task<string> GetClaimAsync(Guid claimId, CancellationToken cancellationToken = default)
        {
            var response = await inner.GetClaimAsync(claimId, cancellationToken);
            collector.Record("memory_recall.get_claim", response);
            return response;
        }

        [KernelFunction("get_evidence")]
        public async Task<string> GetEvidenceAsync(Guid claimId, CancellationToken cancellationToken = default)
        {
            var response = await inner.GetEvidenceAsync(claimId, cancellationToken);
            collector.Record("memory_recall.get_evidence", response);
            return response;
        }
    }

    private sealed class TrackingMemoryWritePlugin(MemoryWritePlugin inner, ToolExecutionCollector collector)
    {
        public async Task<string> IngestNoteForSystemAsync(
            string sourceRef,
            string content,
            string? metadataJson = null,
            string? idempotencyKey = null,
            CancellationToken cancellationToken = default)
        {
            var response = await inner.IngestNoteAsync(sourceRef, content, metadataJson, idempotencyKey, cancellationToken);
            collector.Record("memory_write.ingest_note", response, source: "post_turn");
            return response;
        }

        public async Task<string> CreateClaimForSystemAsync(
            string subjectKey,
            string predicate,
            string literalValue,
            string sourceRef,
            string excerpt,
            double confidence = 0.6,
            string? idempotencyKey = null,
            CancellationToken cancellationToken = default)
        {
            var response = await inner.CreateClaimAsync(subjectKey, predicate, literalValue, sourceRef, excerpt, confidence, idempotencyKey, cancellationToken);
            collector.Record("memory_write.create_claim", response, source: "post_turn");
            return response;
        }

        [KernelFunction("ingest_note")]
        public async Task<string> IngestNoteAsync(
            string sourceRef,
            string content,
            string? metadataJson = null,
            string? idempotencyKey = null,
            CancellationToken cancellationToken = default)
        {
            var response = await inner.IngestNoteAsync(sourceRef, content, metadataJson, idempotencyKey, cancellationToken);
            collector.Record("memory_write.ingest_note", response);
            return response;
        }

        [KernelFunction("create_claim")]
        public async Task<string> CreateClaimAsync(
            string subjectKey,
            string predicate,
            string literalValue,
            string sourceRef,
            string excerpt,
            double confidence = 0.6,
            string? idempotencyKey = null,
            CancellationToken cancellationToken = default)
        {
            var response = await inner.CreateClaimAsync(subjectKey, predicate, literalValue, sourceRef, excerpt, confidence, idempotencyKey, cancellationToken);
            collector.Record("memory_write.create_claim", response);
            return response;
        }
    }

    private sealed class TrackingMemoryGovernancePlugin(MemoryGovernancePlugin inner, ToolExecutionCollector collector)
    {
        [KernelFunction("flag_contradiction")]
        public async Task<string> FlagContradictionAsync(
            Guid claimId,
            string reason,
            string? idempotencyKey = null,
            CancellationToken cancellationToken = default)
        {
            var response = await inner.FlagContradictionAsync(claimId, reason, idempotencyKey, cancellationToken);
            collector.Record("memory_governance.flag_contradiction", response);
            return response;
        }

        [KernelFunction("supersede_claim")]
        public async Task<string> SupersedeClaimAsync(
            Guid claimId,
            Guid replacementClaimId,
            string? idempotencyKey = null,
            CancellationToken cancellationToken = default)
        {
            var response = await inner.SupersedeClaimAsync(claimId, replacementClaimId, idempotencyKey, cancellationToken);
            collector.Record("memory_governance.supersede_claim", response);
            return response;
        }

        [KernelFunction("retract_claim")]
        public async Task<string> RetractClaimAsync(
            Guid claimId,
            string? idempotencyKey = null,
            CancellationToken cancellationToken = default)
        {
            var response = await inner.RetractClaimAsync(claimId, idempotencyKey, cancellationToken);
            collector.Record("memory_governance.retract_claim", response);
            return response;
        }
    }

    private sealed class TrackingGroundingPlugin(GroundingPlugin inner, ToolExecutionCollector collector)
    {
        [KernelFunction("require_citations")]
        public string RequireCitations(string draftAnswer, string citationsJson)
        {
            var response = inner.RequireCitations(draftAnswer, citationsJson);
            collector.Record("grounding.require_citations", response);
            return response;
        }
    }

    private sealed record RecalledClaim(Guid ClaimId, string Predicate, string LiteralValue, double Confidence, double Score);
}
