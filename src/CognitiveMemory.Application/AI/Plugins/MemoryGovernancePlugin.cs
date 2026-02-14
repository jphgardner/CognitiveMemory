using System.Text.Json;
using CognitiveMemory.Application.AI.Tooling;
using CognitiveMemory.Application.Interfaces;
using Microsoft.SemanticKernel;

namespace CognitiveMemory.Application.AI.Plugins;

public sealed class MemoryGovernancePlugin(
    IClaimRepository claimRepository,
    IToolExecutionRepository toolExecutionRepository,
    IOutboxRepository outboxRepository,
    AgentToolingGuard guard)
{
    private const string FlagContradictionTool = "memory_governance.flag_contradiction";
    private const string SupersedeClaimTool = "memory_governance.supersede_claim";
    private const string RetractClaimTool = "memory_governance.retract_claim";

    [KernelFunction("flag_contradiction")]
    public async Task<string> FlagContradictionAsync(
        Guid claimId,
        string reason,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        var traceId = ToolEnvelopeJson.ResolveTraceId();
        var effectiveIdempotencyKey = IdempotencyKeyFactory.Resolve(FlagContradictionTool, idempotencyKey, claimId.ToString("D"), reason);

        var cached = await TryGetCachedResponseAsync(FlagContradictionTool, effectiveIdempotencyKey, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        try
        {
            var response = await guard.RunAsync(async ct =>
            {
                guard.EnsurePrivilegedWriteEnabled();

                var contradictionId = await claimRepository.CreateManualContradictionAsync(claimId, reason, ct);
                var eventId = await outboxRepository.EnqueueAsync(new OutboxEventWriteRequest
                {
                    EventType = OutboxEventTypes.MemoryContradictionFlagged,
                    AggregateType = OutboxAggregateTypes.Contradiction,
                    AggregateId = contradictionId,
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        contradictionId,
                        claimId,
                        reason
                    }),
                    IdempotencyKey = effectiveIdempotencyKey
                }, ct);

                return ToolEnvelopeJson.Success(
                    data: new { contradictionId, claimId, status = "flagged" },
                    code: "created",
                    message: "Contradiction flagged and outbox event emitted.",
                    idempotencyKey: effectiveIdempotencyKey,
                    eventIds: [eventId],
                    traceId: traceId);
            }, cancellationToken);

            await toolExecutionRepository.SaveAsync(FlagContradictionTool, effectiveIdempotencyKey, response, cancellationToken);
            return response;
        }
        catch (Exception ex)
        {
            return ToolEnvelopeJson.Failure(
                code: "flag_contradiction_failed",
                message: ex.Message,
                data: new { claimId },
                idempotencyKey: effectiveIdempotencyKey,
                traceId: traceId);
        }
    }

    [KernelFunction("supersede_claim")]
    public async Task<string> SupersedeClaimAsync(
        Guid claimId,
        Guid replacementClaimId,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        var traceId = ToolEnvelopeJson.ResolveTraceId();
        var effectiveIdempotencyKey = IdempotencyKeyFactory.Resolve(SupersedeClaimTool, idempotencyKey, claimId.ToString("D"), replacementClaimId.ToString("D"));

        var cached = await TryGetCachedResponseAsync(SupersedeClaimTool, effectiveIdempotencyKey, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        try
        {
            var response = await guard.RunAsync(async ct =>
            {
                guard.EnsurePrivilegedWriteEnabled();

                var result = await claimRepository.SupersedeAsync(claimId, replacementClaimId, ct);
                var eventId = await outboxRepository.EnqueueAsync(new OutboxEventWriteRequest
                {
                    EventType = OutboxEventTypes.MemoryClaimSuperseded,
                    AggregateType = OutboxAggregateTypes.Claim,
                    AggregateId = result.ClaimId,
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        claimId = result.ClaimId,
                        replacementClaimId,
                        status = result.Status,
                        updatedAt = result.UpdatedAt
                    }),
                    IdempotencyKey = effectiveIdempotencyKey
                }, ct);

                return ToolEnvelopeJson.Success(
                    data: new { result.ClaimId, result.Status, result.UpdatedAt, replacementClaimId },
                    code: "updated",
                    message: "Claim superseded and outbox event emitted.",
                    idempotencyKey: effectiveIdempotencyKey,
                    eventIds: [eventId],
                    traceId: traceId);
            }, cancellationToken);

            await toolExecutionRepository.SaveAsync(SupersedeClaimTool, effectiveIdempotencyKey, response, cancellationToken);
            return response;
        }
        catch (Exception ex)
        {
            return ToolEnvelopeJson.Failure(
                code: "supersede_claim_failed",
                message: ex.Message,
                data: new { claimId, replacementClaimId },
                idempotencyKey: effectiveIdempotencyKey,
                traceId: traceId);
        }
    }

    [KernelFunction("retract_claim")]
    public async Task<string> RetractClaimAsync(
        Guid claimId,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        var traceId = ToolEnvelopeJson.ResolveTraceId();
        var effectiveIdempotencyKey = IdempotencyKeyFactory.Resolve(RetractClaimTool, idempotencyKey, claimId.ToString("D"));

        var cached = await TryGetCachedResponseAsync(RetractClaimTool, effectiveIdempotencyKey, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        try
        {
            var response = await guard.RunAsync(async ct =>
            {
                guard.EnsurePrivilegedWriteEnabled();

                var result = await claimRepository.RetractAsync(claimId, ct);
                var eventId = await outboxRepository.EnqueueAsync(new OutboxEventWriteRequest
                {
                    EventType = OutboxEventTypes.MemoryClaimRetracted,
                    AggregateType = OutboxAggregateTypes.Claim,
                    AggregateId = result.ClaimId,
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        claimId = result.ClaimId,
                        status = result.Status,
                        updatedAt = result.UpdatedAt
                    }),
                    IdempotencyKey = effectiveIdempotencyKey
                }, ct);

                return ToolEnvelopeJson.Success(
                    data: new { result.ClaimId, result.Status, result.UpdatedAt },
                    code: "updated",
                    message: "Claim retracted and outbox event emitted.",
                    idempotencyKey: effectiveIdempotencyKey,
                    eventIds: [eventId],
                    traceId: traceId);
            }, cancellationToken);

            await toolExecutionRepository.SaveAsync(RetractClaimTool, effectiveIdempotencyKey, response, cancellationToken);
            return response;
        }
        catch (Exception ex)
        {
            return ToolEnvelopeJson.Failure(
                code: "retract_claim_failed",
                message: ex.Message,
                data: new { claimId },
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
