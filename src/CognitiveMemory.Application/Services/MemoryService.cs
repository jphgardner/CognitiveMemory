using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CognitiveMemory.Application.AI;
using CognitiveMemory.Application.AI.Tooling;
using CognitiveMemory.Application.Contracts;
using CognitiveMemory.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace CognitiveMemory.Application.Services;

public partial class MemoryService(
    IDocumentRepository documentRepository,
    IDocumentIngestionPipeline documentIngestionPipeline,
    ITextEmbeddingProvider embeddingProvider,
    IClaimRepository claimRepository,
    IQueryCache queryCache,
    IDebateOrchestrator debateOrchestrator,
    ISystemHealthProbe healthProbe,
    ILogger<MemoryService> logger,
    IOutboxRepository? outboxRepository = null,
    IPolicyDecisionRepository? policyDecisionRepository = null,
    IClaimInsightRepository? claimInsightRepository = null,
    IClaimCalibrationRepository? claimCalibrationRepository = null) : IMemoryService
{
    private const int AnswerGenerationTimeoutSeconds = 25;
    private const string ConversationHistoryContextKey = "conversation_history";
    private const int MaxConversationContextChars = 2400;

    public Task<MemoryHealthResponse> GetHealthAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Fetching memory subsystem health.");
        return healthProbe.CheckAsync(cancellationToken);
    }

    public Task<IReadOnlyList<ClaimListItem>> GetClaimsAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Fetching recent claims.");
        return claimRepository.GetRecentAsync(50, cancellationToken);
    }

    public async Task<ClaimCreatedResponse> CreateClaimAsync(CreateClaimRequest request, CancellationToken cancellationToken)
    {
        ValidateClaimInvariant(request);
        logger.LogInformation("Creating claim for subject {SubjectEntityId} with predicate {Predicate}.", request.SubjectEntityId, request.Predicate);
        var created = await claimRepository.CreateAsync(request, cancellationToken);
        await EmitOutboxEventSafeAsync(
            eventType: OutboxEventTypes.MemoryClaimCreated,
            aggregateType: OutboxAggregateTypes.Claim,
            aggregateId: created.ClaimId,
            payload: new
            {
                claimId = created.ClaimId,
                predicate = created.Predicate,
                confidence = created.Confidence,
                scope = created.Scope
            },
            idempotencyKey: $"{OutboxEventTypes.MemoryClaimCreated}:{created.ClaimId:N}",
            cancellationToken);
        return created;
    }

    public async Task<ClaimLifecycleResponse> SupersedeClaimAsync(Guid claimId, Guid replacementClaimId, CancellationToken cancellationToken)
    {
        logger.LogInformation("Superseding claim {ClaimId} using replacement {ReplacementClaimId}.", claimId, replacementClaimId);
        var result = await claimRepository.SupersedeAsync(claimId, replacementClaimId, cancellationToken);
        await EmitOutboxEventSafeAsync(
            eventType: OutboxEventTypes.MemoryClaimSuperseded,
            aggregateType: OutboxAggregateTypes.Claim,
            aggregateId: result.ClaimId,
            payload: new { claimId = result.ClaimId, replacementClaimId, status = result.Status, updatedAt = result.UpdatedAt },
            idempotencyKey: $"{OutboxEventTypes.MemoryClaimSuperseded}:{result.ClaimId:N}:{replacementClaimId:N}",
            cancellationToken);
        return result;
    }

    public async Task<ClaimLifecycleResponse> RetractClaimAsync(Guid claimId, CancellationToken cancellationToken)
    {
        logger.LogInformation("Retracting claim {ClaimId}.", claimId);
        var result = await claimRepository.RetractAsync(claimId, cancellationToken);
        await EmitOutboxEventSafeAsync(
            eventType: OutboxEventTypes.MemoryClaimRetracted,
            aggregateType: OutboxAggregateTypes.Claim,
            aggregateId: result.ClaimId,
            payload: new { claimId = result.ClaimId, status = result.Status, updatedAt = result.UpdatedAt },
            idempotencyKey: $"{OutboxEventTypes.MemoryClaimRetracted}:{result.ClaimId:N}",
            cancellationToken);
        return result;
    }

    public async Task<IngestDocumentResponse> IngestDocumentAsync(IngestDocumentRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
        {
            throw new ArgumentException("content is required", nameof(request));
        }

        var metadata = request.Metadata ?? new Dictionary<string, string>();

        var existingBySourceRef = await documentRepository.GetBySourceRefAsync(request.SourceType, request.SourceRef, cancellationToken);
        if (existingBySourceRef is not null)
        {
            logger.LogInformation("Skipping duplicate ingest by source ref for {SourceType}:{SourceRef}.", request.SourceType, request.SourceRef);
            return new IngestDocumentResponse
            {
                DocumentId = existingBySourceRef.DocumentId,
                Status = "Queued",
                ClaimsCreated = 0
            };
        }

        var contentHash = MemoryIdentity.ComputeContentHash(request.Content);
        var existing = await documentRepository.GetBySourceHashAsync(request.SourceType, request.SourceRef, contentHash, cancellationToken);
        if (existing is not null)
        {
            logger.LogInformation("Skipping duplicate document ingest for {SourceType}:{SourceRef}.", request.SourceType, request.SourceRef);
            return new IngestDocumentResponse
            {
                DocumentId = existing.DocumentId,
                Status = "Queued",
                ClaimsCreated = 0
            };
        }

        var metadataJson = JsonSerializer.Serialize(metadata);

        var document = await documentRepository.CreateAsync(
            request.SourceType,
            request.SourceRef,
            request.Content,
            metadataJson,
            contentHash,
            cancellationToken);
        logger.LogInformation("Document {DocumentId} persisted for {SourceType}:{SourceRef}. Starting claim extraction.", document.DocumentId, request.SourceType, request.SourceRef);
        await EmitOutboxEventSafeAsync(
            eventType: OutboxEventTypes.MemoryDocumentIngested,
            aggregateType: OutboxAggregateTypes.Document,
            aggregateId: document.DocumentId,
            payload: new
            {
                documentId = document.DocumentId,
                request.SourceType,
                request.SourceRef,
                contentHash,
                capturedAt = document.CapturedAt
            },
            idempotencyKey: $"{OutboxEventTypes.MemoryDocumentIngested}:{request.SourceType}:{request.SourceRef}:{contentHash}",
            cancellationToken);

        var createdClaims = await documentIngestionPipeline.ProcessDocumentAsync(document, cancellationToken);

        return new IngestDocumentResponse
        {
            DocumentId = document.DocumentId,
            Status = "Queued",
            ClaimsCreated = createdClaims
        };
    }

    public async Task<QueryClaimsResponse> QueryClaimsAsync(QueryClaimsRequest request, string requestId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            throw new ArgumentException("text is required", nameof(request));
        }

        if (request.TopK is < 1 or > 50)
        {
            throw new ArgumentOutOfRangeException(nameof(request.TopK), "topK must be between 1 and 50");
        }

        var startedAt = DateTimeOffset.UtcNow;
        var subjectFilter = request.Filters.Subject;
        var cacheKey = BuildQueryCacheKey(request);
        var cached = await queryCache.GetAsync(cacheKey, cancellationToken);
        if (cached is not null)
        {
            logger.LogInformation("Query cache hit for key {CacheKey}.", cacheKey);
            return RehydrateCachedResponse(cached, requestId);
        }

        var candidates = await claimRepository.GetQueryCandidatesAsync(subjectFilter, cancellationToken);
        var candidateIds = candidates.Select(c => c.ClaimId).ToHashSet();
        var insightMap = claimInsightRepository is null
            ? new Dictionary<Guid, ClaimInsightRecord>()
            : (await claimInsightRepository.GetByClaimIdsAsync(candidateIds, cancellationToken)).ToDictionary(kv => kv.Key, kv => kv.Value);
        var calibrationMap = claimCalibrationRepository is null
            ? new Dictionary<Guid, ClaimCalibrationRecord>()
            : (await claimCalibrationRepository.GetLatestByClaimIdsAsync(candidateIds, cancellationToken)).ToDictionary(kv => kv.Key, kv => kv.Value);

        var queryEmbedding = await embeddingProvider.GenerateEmbeddingAsync(request.Text, cancellationToken);
        var scored = new List<QueryClaimItem>();

        foreach (var candidate in candidates.Where(c => c.Evidence.Count > 0))
        {
            var insight = insightMap.GetValueOrDefault(candidate.ClaimId);
            var candidateText = BuildCandidateRetrievalText(candidate, insight);
            var retrievalRelevance = await ComputeRetrievalRelevance(request.Text, candidateText, queryEmbedding, cancellationToken);
            var evidenceStrength = candidate.Evidence.Average(e => e.Strength);
            var contradictionPenalty = QueryScoring.ComputeContradictionPenalty(candidate.Contradictions);
            var recencyBoost = QueryScoring.ComputeRecencyBoost(candidate.LastReinforcedAt ?? candidate.ValidFrom);
            var stalenessPenalty = QueryScoring.ComputeStalenessPenalty(candidate.ValidTo);
            var scopeMatchBoost = QueryScoring.ComputeScopeMatchBoost(subjectFilter, candidate.Scope);
            var effectiveConfidence = calibrationMap.GetValueOrDefault(candidate.ClaimId)?.RecommendedConfidence ?? candidate.Confidence;

            var score = QueryScoring.ComputeScore(
                effectiveConfidence,
                evidenceStrength,
                retrievalRelevance,
                recencyBoost,
                scopeMatchBoost,
                contradictionPenalty,
                stalenessPenalty);

            scored.Add(new QueryClaimItem
            {
                ClaimId = candidate.ClaimId,
                Predicate = candidate.Predicate,
                LiteralValue = candidate.LiteralValue,
                Confidence = effectiveConfidence,
                Score = score,
                Evidence = request.IncludeEvidence ? candidate.Evidence : [],
                Contradictions = request.IncludeContradictions ? candidate.Contradictions : []
            });
        }

        var ordered = scored
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.ClaimId)
            .Take(request.TopK)
            .ToList();

        var uncertaintyFlags = QueryScoring.ComputeUncertaintyFlags(ordered);
        var latency = (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;

        var response = new QueryClaimsResponse
        {
            Claims = ordered,
            Meta = new QueryMeta
            {
                Strategy = "hybrid",
                LatencyMs = latency,
                RequestId = requestId,
                UncertaintyFlags = uncertaintyFlags
            }
        };

        await queryCache.SetAsync(cacheKey, response, TimeSpan.FromSeconds(120), cancellationToken);
        return response;
    }

    public async Task<AnswerQuestionResponse> AnswerAsync(AnswerQuestionRequest request, string requestId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
        {
            throw new ArgumentException("question is required", nameof(request));
        }

        var conversationHistory = request.Context.GetValueOrDefault(ConversationHistoryContextKey);
        var retrievalQuery = BuildRetrievalQueryText(request.Question, conversationHistory);
        var debateQuestion = BuildDebateQuestionText(request.Question, conversationHistory);
        var subjectFilter = request.Context.GetValueOrDefault("project");
        var queryResponse = await QueryClaimsAsync(new QueryClaimsRequest
        {
            Text = retrievalQuery,
            Filters = new QueryFilters { Subject = subjectFilter },
            TopK = 10,
            IncludeEvidence = true,
            IncludeContradictions = true
        }, requestId, cancellationToken);

        DebateResult debated;
        var fallbackUsed = false;
        using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(AnswerGenerationTimeoutSeconds)))
        using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token))
        {
            try
            {
                debated = await debateOrchestrator.OrchestrateAsync(debateQuestion, queryResponse, linkedCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
            {
                logger.LogWarning("Debate orchestration timed out after {TimeoutSeconds}s for request {RequestId}. Using fallback answer.", AnswerGenerationTimeoutSeconds, requestId);
                debated = BuildAnswerFallback(queryResponse);
                fallbackUsed = true;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Debate orchestration failed for request {RequestId}. Using fallback answer.", requestId);
                debated = BuildAnswerFallback(queryResponse);
                fallbackUsed = true;
            }
        }

        if (string.IsNullOrWhiteSpace(debated.Answer))
        {
            debated = new DebateResult
            {
                Citations = debated.Citations,
                Contradictions = debated.Contradictions,
                Answer = "Based on available evidence, I cannot provide a confident answer yet.",
                Confidence = Math.Min(debated.Confidence, 0.5),
                UncertaintyFlags = debated.UncertaintyFlags.Concat([ConscienceReasonCodes.InsufficientEvidence]).Distinct().ToList()
            };
            fallbackUsed = true;
        }

        var conscience = BuildConscienceDecision(debated);

        await PersistPolicyDecisionSafeAsync(
            sourceType: PolicyDecisionSources.ChatAnswer,
            sourceRef: requestId,
            conscience: conscience,
            reasonCodes: conscience.ReasonCodes,
            metadata: new
            {
                question = request.Question,
                conversationContextChars = string.IsNullOrWhiteSpace(conversationHistory) ? 0 : conversationHistory.Length,
                citations = debated.Citations.Count,
                contradictions = debated.Contradictions.Count,
                debated.UncertaintyFlags
            },
            cancellationToken);
        await EmitOutboxEventSafeAsync(
            eventType: OutboxEventTypes.MemoryAnswerGenerated,
            aggregateType: OutboxAggregateTypes.Answer,
            aggregateId: MemoryIdentity.ComputeStableGuid(requestId),
            payload: new
            {
                requestId,
                question = request.Question,
                conversationContextChars = string.IsNullOrWhiteSpace(conversationHistory) ? 0 : conversationHistory.Length,
                fallbackUsed,
                decision = conscience.Decision,
                conscience.RiskScore,
                reasonCodes = conscience.ReasonCodes
            },
            idempotencyKey: $"{OutboxEventTypes.MemoryAnswerGenerated}:{requestId}",
            cancellationToken);

        return new AnswerQuestionResponse
        {
            Answer = debated.Answer,
            Confidence = debated.Confidence,
            Citations = debated.Citations,
            UncertaintyFlags = debated.UncertaintyFlags,
            Contradictions = debated.Contradictions,
            Conscience = conscience,
            RequestId = requestId
        };
    }

}
