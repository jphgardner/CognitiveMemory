using System.Text.Json;
using CognitiveMemory.Application.AI.Tooling;
using CognitiveMemory.Application.Contracts;
using Microsoft.Extensions.Logging;

namespace CognitiveMemory.Application.Services;

public partial class MemoryService
{
    private async Task EmitOutboxEventSafeAsync(
        string eventType,
        string aggregateType,
        Guid aggregateId,
        object payload,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (outboxRepository is null)
        {
            return;
        }

        try
        {
            await outboxRepository.EnqueueAsync(new OutboxEventWriteRequest
            {
                EventType = eventType,
                AggregateType = aggregateType,
                AggregateId = aggregateId,
                PayloadJson = JsonSerializer.Serialize(payload),
                IdempotencyKey = idempotencyKey
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to enqueue outbox event {EventType} for aggregate {AggregateId}.", eventType, aggregateId);
        }
    }

    private async Task PersistPolicyDecisionSafeAsync(
        string sourceType,
        string sourceRef,
        AnswerConscience conscience,
        IReadOnlyList<string> reasonCodes,
        object metadata,
        CancellationToken cancellationToken)
    {
        if (policyDecisionRepository is null)
        {
            return;
        }

        try
        {
            await policyDecisionRepository.SaveAsync(new PolicyDecisionWriteRequest
            {
                SourceType = sourceType,
                SourceRef = sourceRef,
                Decision = conscience.Decision,
                RiskScore = conscience.RiskScore,
                PolicyVersion = conscience.PolicyVersion,
                ReasonCodes = reasonCodes,
                MetadataJson = JsonSerializer.Serialize(metadata)
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist policy decision for {SourceType}:{SourceRef}.", sourceType, sourceRef);
        }
    }

}
