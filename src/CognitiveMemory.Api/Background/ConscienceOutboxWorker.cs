using System.Text.Json;
using CognitiveMemory.Application.AI;
using CognitiveMemory.Application.AI.Tooling;
using CognitiveMemory.Application.Interfaces;
using CognitiveMemory.Application.Services;
using Microsoft.Extensions.Options;

namespace CognitiveMemory.Api.Background;

public sealed class ConscienceOutboxWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<ConscienceOutboxWorkerOptions> options,
    IOptions<ConscienceCalibrationOptions> calibrationOptions,
    ILogger<ConscienceOutboxWorker> logger) : BackgroundService
{
    private static readonly HashSet<string> ConscienceCandidateEvents = new(StringComparer.Ordinal)
    {
        OutboxEventTypes.MemoryClaimCreated,
        OutboxEventTypes.MemoryContradictionFlagged,
        OutboxEventTypes.MemoryClaimSuperseded,
        OutboxEventTypes.MemoryClaimRetracted
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollDelay = TimeSpan.FromSeconds(Math.Max(1, options.Value.PollIntervalSeconds));
        var leaseDuration = TimeSpan.FromSeconds(Math.Max(5, options.Value.LeaseSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var outboxRepository = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();

                var batch = await outboxRepository.ReservePendingAsync(
                    batchSize: options.Value.BatchSize,
                    leaseDuration: leaseDuration,
                    cancellationToken: stoppingToken);

                if (batch.Count == 0)
                {
                    await Task.Delay(pollDelay, stoppingToken);
                    continue;
                }

                foreach (var item in batch)
                {
                    await ProcessOneAsync(item, scope.ServiceProvider, outboxRepository, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Outbox worker loop failed.");
                await Task.Delay(pollDelay, stoppingToken);
            }
        }
    }

    private async Task ProcessOneAsync(
        OutboxEventRecord item,
        IServiceProvider services,
        IOutboxRepository outboxRepository,
        CancellationToken cancellationToken)
    {
        try
        {
            var handled = await HandleEventAsync(item, services, cancellationToken);
            await outboxRepository.MarkSucceededAsync(item.EventId, cancellationToken);

            if (handled)
            {
                logger.LogInformation("Outbox worker processed event {EventType} ({EventId}).", item.EventType, item.EventId);
            }
        }
        catch (Exception ex)
        {
            await outboxRepository.MarkFailedAsync(
                eventId: item.EventId,
                error: ex.Message,
                retryDelay: TimeSpan.FromSeconds(Math.Max(5, options.Value.RetryDelaySeconds)),
                cancellationToken: cancellationToken);

            logger.LogWarning(ex, "Outbox worker failed processing event {EventType} ({EventId}).", item.EventType, item.EventId);
        }
    }

    private static bool IsConscienceCandidateEvent(string eventType) => ConscienceCandidateEvents.Contains(eventType);

    private async Task<bool> HandleEventAsync(OutboxEventRecord item, IServiceProvider services, CancellationToken cancellationToken)
    {
        if (string.Equals(item.EventType, OutboxEventTypes.MemoryDocumentIngested, StringComparison.Ordinal))
        {
            return await HandleDocumentIngestedAsync(item, services, cancellationToken);
        }

        if (!IsConscienceCandidateEvent(item.EventType))
        {
            return false;
        }

        var claimId = ResolveClaimId(item);
        if (!claimId.HasValue)
        {
            return false;
        }

        var claimRepository = services.GetRequiredService<IClaimRepository>();
        var policyDecisionRepository = services.GetRequiredService<IPolicyDecisionRepository>();
        var outboxRepository = services.GetRequiredService<IOutboxRepository>();
        var conscienceAnalysisEngine = services.GetRequiredService<IConscienceAnalysisEngine>();
        var claimInsightRepository = services.GetRequiredService<IClaimInsightRepository>();
        var claimCalibrationRepository = services.GetRequiredService<IClaimCalibrationRepository>();

        var claim = await claimRepository.GetQueryCandidateByIdAsync(claimId.Value, cancellationToken);
        if (claim is null)
        {
            return false;
        }

        var analysis = await conscienceAnalysisEngine.AnalyzeClaimAsync(new ConscienceAnalysisInput
        {
            SourceEventId = item.EventId,
            SourceEventType = item.EventType,
            Claim = claim
        }, cancellationToken);

        await policyDecisionRepository.SaveAsync(new PolicyDecisionWriteRequest
        {
            SourceType = PolicyDecisionSources.ConscienceWorker,
            SourceRef = item.EventId.ToString("D"),
            Decision = analysis.Decision,
            RiskScore = analysis.RiskScore,
            PolicyVersion = ConsciencePolicy.CurrentVersion,
            ReasonCodes = analysis.ReasonCodes,
            MetadataJson = JsonSerializer.Serialize(new
            {
                item.EventId,
                item.EventType,
                claimId = claim.ClaimId,
                currentConfidence = claim.Confidence,
                analysis.RecommendedConfidence,
                analysis.Summary,
                analysis.Keywords,
                analysis.UsedFallback,
                analysis.ModelId
            })
        }, cancellationToken);

        await claimInsightRepository.UpsertAsync(new ClaimInsightRecord
        {
            ClaimId = claim.ClaimId,
            Summary = string.IsNullOrWhiteSpace(analysis.Summary)
                ? $"Claim '{claim.Predicate}' => '{claim.LiteralValue}'."
                : analysis.Summary,
            Keywords = analysis.Keywords,
            SourceEventRef = item.EventId.ToString("N"),
            UpdatedAt = DateTimeOffset.UtcNow
        }, cancellationToken);

        await claimCalibrationRepository.AddAsync(new ClaimCalibrationRecord
        {
            ClaimId = claim.ClaimId,
            RecommendedConfidence = analysis.RecommendedConfidence,
            SourceEventRef = item.EventId.ToString("N"),
            ReasonCodesJson = JsonStringArrayCodec.Serialize(analysis.ReasonCodes),
            CreatedAt = DateTimeOffset.UtcNow
        }, cancellationToken);

        var confidenceUpdate = await TryApplyConfidenceWriteBackAsync(
            claimRepository,
            claim.ClaimId,
            analysis,
            cancellationToken);

        await EnqueueClaimEventAsync(
            outboxRepository,
            OutboxEventTypes.MemoryClaimEnriched,
            claim.ClaimId,
            item.EventId,
            new
            {
                sourceEventId = item.EventId,
                claimId = claim.ClaimId,
                summary = analysis.Summary,
                keywords = analysis.Keywords,
                modelId = analysis.ModelId,
                fallback = analysis.UsedFallback
            },
            cancellationToken);

        await EnqueueClaimEventAsync(
            outboxRepository,
            OutboxEventTypes.MemoryClaimCalibrationRecorded,
            claim.ClaimId,
            item.EventId,
            new
            {
                sourceEventId = item.EventId,
                claimId = claim.ClaimId,
                recommendedConfidence = analysis.RecommendedConfidence,
                decision = analysis.Decision,
                reasonCodes = analysis.ReasonCodes
            },
            cancellationToken);

        if (confidenceUpdate is { Applied: true })
        {
            await EnqueueClaimEventAsync(
                outboxRepository,
                OutboxEventTypes.MemoryClaimConfidenceUpdated,
                claim.ClaimId,
                item.EventId,
                new
                {
                    sourceEventId = item.EventId,
                    claimId = claim.ClaimId,
                    previousConfidence = confidenceUpdate.PreviousConfidence,
                    updatedConfidence = confidenceUpdate.UpdatedConfidence,
                    analysis.Decision,
                    analysis.RiskScore
                },
                cancellationToken);
        }

        await EnqueueClaimEventAsync(
            outboxRepository,
            OutboxEventTypes.ConscienceAnalysisCompleted,
            claim.ClaimId,
            item.EventId,
            new
            {
                sourceEventId = item.EventId,
                claimId = claim.ClaimId,
                analysis.Decision,
                analysis.RecommendedConfidence,
                analysis.RiskScore,
                analysis.ReasonCodes
            },
            cancellationToken);

        return true;
    }

    private async Task<bool> HandleDocumentIngestedAsync(
        OutboxEventRecord item,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var documentId = ResolveDocumentId(item);
        if (!documentId.HasValue)
        {
            return false;
        }

        var documentRepository = services.GetRequiredService<IDocumentRepository>();
        var ingestionPipeline = services.GetRequiredService<IDocumentIngestionPipeline>();

        var document = await documentRepository.GetByIdAsync(documentId.Value, cancellationToken);
        if (document is null)
        {
            return false;
        }

        var createdClaims = await ingestionPipeline.ProcessDocumentAsync(document, cancellationToken);
        logger.LogInformation(
            "Processed document-ingested event {EventId} for document {DocumentId}. Created {ClaimCount} claims.",
            item.EventId,
            document.DocumentId,
            createdClaims);
        return true;
    }

    private static Task<Guid> EnqueueClaimEventAsync(
        IOutboxRepository outboxRepository,
        string eventType,
        Guid claimId,
        Guid sourceEventId,
        object payload,
        CancellationToken cancellationToken)
    {
        return outboxRepository.EnqueueAsync(new OutboxEventWriteRequest
        {
            EventType = eventType,
            AggregateType = OutboxAggregateTypes.Claim,
            AggregateId = claimId,
            IdempotencyKey = $"{eventType}:{sourceEventId:N}",
            PayloadJson = JsonSerializer.Serialize(payload)
        }, cancellationToken);
    }

    private async Task<ClaimConfidenceUpdateResult?> TryApplyConfidenceWriteBackAsync(
        IClaimRepository claimRepository,
        Guid claimId,
        ConscienceAnalysisResult analysis,
        CancellationToken cancellationToken)
    {
        var settings = calibrationOptions.Value;
        if (!settings.EnableClaimConfidenceWriteBack)
        {
            return null;
        }

        if (analysis.RiskScore > settings.MaxRiskScoreForWriteBack)
        {
            return null;
        }

        var decisionAllowed = settings.AllowedDecisions
            .Contains(analysis.Decision, StringComparer.OrdinalIgnoreCase);
        if (!decisionAllowed)
        {
            return null;
        }

        try
        {
            return await claimRepository.TryApplyRecommendedConfidenceAsync(
                claimId,
                analysis.RecommendedConfidence,
                settings.MinDeltaToWriteBack,
                settings.MaxStepPerUpdate,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Conscience confidence write-back failed for claim {ClaimId}.", claimId);
            return null;
        }
    }

    private static Guid? ResolveClaimId(OutboxEventRecord item)
    {
        if (string.Equals(item.AggregateType, OutboxAggregateTypes.Claim, StringComparison.OrdinalIgnoreCase) && item.AggregateId.HasValue)
        {
            return item.AggregateId.Value;
        }

        if (string.IsNullOrWhiteSpace(item.PayloadJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(item.PayloadJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("claimId", out var claimIdEl) && claimIdEl.ValueKind == JsonValueKind.String && Guid.TryParse(claimIdEl.GetString(), out var claimId))
            {
                return claimId;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static Guid? ResolveDocumentId(OutboxEventRecord item)
    {
        if (string.Equals(item.AggregateType, OutboxAggregateTypes.Document, StringComparison.OrdinalIgnoreCase) && item.AggregateId.HasValue)
        {
            return item.AggregateId.Value;
        }

        if (string.IsNullOrWhiteSpace(item.PayloadJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(item.PayloadJson);
            var root = document.RootElement;
            if (root.TryGetProperty("documentId", out var documentIdElement) &&
                documentIdElement.ValueKind == JsonValueKind.String &&
                Guid.TryParse(documentIdElement.GetString(), out var documentId))
            {
                return documentId;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }
}
