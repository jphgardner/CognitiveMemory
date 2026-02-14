using System.Text.Json;
using CognitiveMemory.Application.AI;
using CognitiveMemory.Application.AI.Tooling;
using CognitiveMemory.Application.Contracts;
using CognitiveMemory.Application.Interfaces;
using Microsoft.SemanticKernel;

namespace CognitiveMemory.Application.AI.Plugins;

public sealed class MemoryWritePlugin(
    IDocumentRepository documentRepository,
    IClaimRepository claimRepository,
    IEntityRepository entityRepository,
    IToolExecutionRepository toolExecutionRepository,
    IOutboxRepository outboxRepository,
    AgentToolingGuard guard)
{
    private const string IngestNoteTool = "memory_write.ingest_note";
    private const string CreateClaimTool = "memory_write.create_claim";

    [KernelFunction("ingest_note")]
    public async Task<string> IngestNoteAsync(
        string sourceRef,
        string content,
        string? metadataJson = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        var traceId = ToolEnvelopeJson.ResolveTraceId();
        var effectiveIdempotencyKey = IdempotencyKeyFactory.Resolve(IngestNoteTool, idempotencyKey, sourceRef, content, metadataJson ?? string.Empty);

        var cached = await TryGetCachedResponseAsync(IngestNoteTool, effectiveIdempotencyKey, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        try
        {
            var response = await guard.RunAsync(async ct =>
            {
                guard.EnsureWriteEnabled();

                if (string.IsNullOrWhiteSpace(sourceRef) || string.IsNullOrWhiteSpace(content))
                {
                    return ToolEnvelopeJson.Failure(
                        code: "validation_error",
                        message: "sourceRef and content are required",
                        idempotencyKey: effectiveIdempotencyKey,
                        traceId: traceId);
                }

                const string sourceType = "AgentNote";
                var existingByRef = await documentRepository.GetBySourceRefAsync(sourceType, sourceRef, ct);
                if (existingByRef is not null)
                {
                    return ToolEnvelopeJson.Success(
                        data: new { documentId = existingByRef.DocumentId, status = "existing" },
                        code: "already_exists",
                        message: "Document already exists for sourceRef.",
                        idempotencyKey: effectiveIdempotencyKey,
                        traceId: traceId);
                }

                var contentHash = MemoryIdentity.ComputeContentHash(content);
                var existing = await documentRepository.GetBySourceHashAsync(sourceType, sourceRef, contentHash, ct);
                if (existing is not null)
                {
                    return ToolEnvelopeJson.Success(
                        data: new { documentId = existing.DocumentId, status = "existing" },
                        code: "already_exists",
                        message: "Document already exists for source hash.",
                        idempotencyKey: effectiveIdempotencyKey,
                        traceId: traceId);
                }

                var metadata = string.IsNullOrWhiteSpace(metadataJson) ? "{}" : metadataJson;
                var created = await documentRepository.CreateAsync(sourceType, sourceRef, content, metadata, contentHash, ct);

                var eventId = await outboxRepository.EnqueueAsync(new OutboxEventWriteRequest
                {
                    EventType = OutboxEventTypes.MemoryDocumentIngested,
                    AggregateType = OutboxAggregateTypes.Document,
                    AggregateId = created.DocumentId,
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        documentId = created.DocumentId,
                        sourceType,
                        sourceRef,
                        contentHash,
                        capturedAt = created.CapturedAt
                    }),
                    IdempotencyKey = effectiveIdempotencyKey
                }, ct);

                return ToolEnvelopeJson.Success(
                    data: new { documentId = created.DocumentId, status = "created" },
                    code: "created",
                    message: "Document created and outbox event emitted.",
                    idempotencyKey: effectiveIdempotencyKey,
                    eventIds: [eventId],
                    traceId: traceId);
            }, cancellationToken);

            await toolExecutionRepository.SaveAsync(IngestNoteTool, effectiveIdempotencyKey, response, cancellationToken);
            return response;
        }
        catch (Exception ex)
        {
            return ToolEnvelopeJson.Failure(
                code: "ingest_note_failed",
                message: ex.Message,
                data: new { sourceRef },
                idempotencyKey: effectiveIdempotencyKey,
                traceId: traceId);
        }
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
        var traceId = ToolEnvelopeJson.ResolveTraceId();
        var effectiveIdempotencyKey = IdempotencyKeyFactory.Resolve(CreateClaimTool, idempotencyKey, subjectKey, predicate, literalValue, sourceRef, excerpt);

        var cached = await TryGetCachedResponseAsync(CreateClaimTool, effectiveIdempotencyKey, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        try
        {
            var response = await guard.RunAsync(async ct =>
            {
                guard.EnsureWriteEnabled();

                if (string.IsNullOrWhiteSpace(subjectKey) || string.IsNullOrWhiteSpace(predicate) || string.IsNullOrWhiteSpace(literalValue))
                {
                    return ToolEnvelopeJson.Failure(
                        code: "validation_error",
                        message: "subjectKey, predicate, and literalValue are required",
                        idempotencyKey: effectiveIdempotencyKey,
                        traceId: traceId);
                }

                var claimHash = MemoryIdentity.ComputeClaimHash(subjectKey, predicate, literalValue);
                var duplicate = await claimRepository.GetByHashAsync(claimHash, ct);
                if (duplicate is not null)
                {
                    return ToolEnvelopeJson.Success(
                        data: new { claimId = duplicate.ClaimId, status = "existing" },
                        code: "already_exists",
                        message: "Claim hash already exists.",
                        idempotencyKey: effectiveIdempotencyKey,
                        traceId: traceId);
                }

                var subjectId = MemoryIdentity.ComputeStableGuid(subjectKey);
                await entityRepository.UpsertAsync(
                    entityId: subjectId,
                    type: "Concept",
                    name: subjectKey.Trim(),
                    aliases: null,
                    metadata: JsonSerializer.Serialize(new
                    {
                        source = "memory_write.create_claim",
                        subjectKey
                    }),
                    cancellationToken: ct);

                var request = new CreateClaimRequest
                {
                    SubjectEntityId = subjectId,
                    Predicate = predicate,
                    LiteralValue = literalValue,
                    Confidence = Math.Clamp(confidence, 0.1, 1.0),
                    Scope = "{\"source\":\"agent\"}",
                    Hash = claimHash,
                    ValueType = "String",
                    Evidence =
                    [
                        new CreateEvidenceRequest
                        {
                            SourceType = "AgentTool",
                            SourceRef = string.IsNullOrWhiteSpace(sourceRef) ? "agent:auto" : sourceRef,
                            ExcerptOrSummary = string.IsNullOrWhiteSpace(excerpt) ? literalValue : excerpt,
                            Strength = Math.Clamp(confidence, 0.1, 1.0),
                            CapturedAt = DateTimeOffset.UtcNow,
                            Metadata = "{\"tool\":\"create_claim\"}"
                        }
                    ]
                };

                var created = await claimRepository.CreateAsync(request, ct);

                var eventId = await outboxRepository.EnqueueAsync(new OutboxEventWriteRequest
                {
                    EventType = OutboxEventTypes.MemoryClaimCreated,
                    AggregateType = OutboxAggregateTypes.Claim,
                    AggregateId = created.ClaimId,
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        claimId = created.ClaimId,
                        predicate = created.Predicate,
                        confidence = created.Confidence,
                        sourceRef = string.IsNullOrWhiteSpace(sourceRef) ? "agent:auto" : sourceRef
                    }),
                    IdempotencyKey = effectiveIdempotencyKey
                }, ct);

                return ToolEnvelopeJson.Success(
                    data: new { claimId = created.ClaimId, status = "created" },
                    code: "created",
                    message: "Claim created and outbox event emitted.",
                    idempotencyKey: effectiveIdempotencyKey,
                    eventIds: [eventId],
                    traceId: traceId);
            }, cancellationToken);

            await toolExecutionRepository.SaveAsync(CreateClaimTool, effectiveIdempotencyKey, response, cancellationToken);
            return response;
        }
        catch (Exception ex)
        {
            return ToolEnvelopeJson.Failure(
                code: "create_claim_failed",
                message: ex.Message,
                data: new { subjectKey, predicate },
                idempotencyKey: effectiveIdempotencyKey,
                traceId: traceId);
        }
    }

    private async Task<string?> TryGetCachedResponseAsync(string toolName, string idempotencyKey, CancellationToken cancellationToken)
    {
        var existing = await toolExecutionRepository.GetAsync(toolName, idempotencyKey, cancellationToken);
        return existing?.ResponseJson;
    }
}
