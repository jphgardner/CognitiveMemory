using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text;
using System.Text.Json;
using System.Globalization;
using CognitiveMemory.Application.Abstractions;
using CognitiveMemory.Application.Cognitive;
using CognitiveMemory.Infrastructure.Events;
using CognitiveMemory.Infrastructure.Companions;
using CognitiveMemory.Infrastructure.Persistence;
using CognitiveMemory.Infrastructure.Persistence.Entities;
using CognitiveMemory.Infrastructure.Scheduling;
using CognitiveMemory.Infrastructure.SemanticKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace CognitiveMemory.Infrastructure.Subconscious;

public sealed class SubconsciousDebateService(
    MemoryDbContext dbContext,
    IOutboxWriter outboxWriter,
    IWorkingMemoryStore workingMemoryStore,
    IEpisodicMemoryRepository episodicMemoryRepository,
    ISemanticMemoryRepository semanticMemoryRepository,
    IProceduralMemoryRepository proceduralMemoryRepository,
    ISelfModelRepository selfModelRepository,
    ICompanionScopeResolver companionScopeResolver,
    ICompanionCognitiveProfileResolver cognitiveProfileResolver,
    IScheduledActionStore scheduledActionStore,
    SemanticKernelFactory kernelFactory,
    SubconsciousGroupChatManager groupChatManager,
    SubconsciousDebateOptions options,
    ISubconsciousOutcomeValidator outcomeValidator,
    ISubconsciousOutcomeApplier outcomeApplier,
    ILogger<SubconsciousDebateService> logger) : ISubconsciousDebateService
{
    private static readonly ActivitySource ActivitySource = new("CognitiveMemory.Subconscious");
    private static readonly Meter Meter = new("CognitiveMemory.Subconscious");
    private static readonly Counter<long> DebatesQueued = Meter.CreateCounter<long>("subconscious.debates.queued");
    private static readonly Counter<long> DebatesStarted = Meter.CreateCounter<long>("subconscious.debates.started");
    private static readonly Counter<long> DebatesCompleted = Meter.CreateCounter<long>("subconscious.debates.completed");
    private static readonly Counter<long> DebatesAwaitingUser = Meter.CreateCounter<long>("subconscious.debates.awaiting_user");
    private static readonly Counter<long> DebatesFailed = Meter.CreateCounter<long>("subconscious.debates.failed");
    private static readonly Counter<long> DebateTurns = Meter.CreateCounter<long>("subconscious.debate.turns");
    private static readonly Histogram<double> DebateDurationMs = Meter.CreateHistogram<double>("subconscious.debate.duration.ms");
    private static readonly HashSet<string> GenericTopicKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        string.Empty,
        "context-refinement",
        "manual",
        "manual-check",
        "scheduled-topic"
    };

    public async Task QueueDebateAsync(string sessionId, SubconsciousDebateTopic topic, CancellationToken cancellationToken = default)
    {
        if (!options.Enabled || string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        using var activity = ActivitySource.StartActivity("subconscious.queue", ActivityKind.Internal);
        var normalizedSessionId = sessionId.Trim();
        var effectiveTopicKey = ResolveTopicKey(topic);
        var companionId = await companionScopeResolver.ResolveCompanionIdOrThrowAsync(normalizedSessionId, cancellationToken);
        var cognitiveProfile = await ResolveCognitiveProfileAsync(normalizedSessionId, cancellationToken);
        var effectiveDebounceSeconds = ComputeDebounceSeconds(cognitiveProfile.Profile.Reflection.Debate.TriggerSensitivity);
        var now = DateTimeOffset.UtcNow;
        var debounceCutoff = now.AddSeconds(-Math.Max(1, effectiveDebounceSeconds));
        activity?.SetTag("session.id", normalizedSessionId);
        activity?.SetTag("topic.key", effectiveTopicKey);
        activity?.SetTag("trigger.event.type", topic.TriggerEventType);
        activity?.SetTag("trigger.event.id", topic.TriggerEventId?.ToString());

        if (topic.TriggerEventId.HasValue)
        {
            var sameTriggerAlreadyQueued = await dbContext.SubconsciousDebateSessions
                .AsNoTracking()
                .AnyAsync(
                    x => x.SessionId == normalizedSessionId
                         && x.TopicKey == effectiveTopicKey
                         && x.TriggerEventId == topic.TriggerEventId,
                    cancellationToken);
            if (sameTriggerAlreadyQueued)
            {
                return;
            }
        }

        var duplicate = await dbContext.SubconsciousDebateSessions
            .AsNoTracking()
            .AnyAsync(
                x => x.SessionId == normalizedSessionId
                     && x.TopicKey == effectiveTopicKey
                     && x.CreatedAtUtc >= debounceCutoff
                     && (x.State == SubconsciousSessionState.Queued.ToString()
                         || x.State == SubconsciousSessionState.Running.ToString()
                         || x.State == SubconsciousSessionState.AwaitingUser.ToString()),
                cancellationToken);

        if (duplicate)
        {
            return;
        }

        var entity = new SubconsciousDebateSessionEntity
        {
            DebateId = Guid.NewGuid(),
            CompanionId = companionId,
            SessionId = normalizedSessionId,
            TopicKey = effectiveTopicKey,
            TriggerEventId = topic.TriggerEventId,
            TriggerEventType = topic.TriggerEventType,
            TriggerPayloadJson = string.IsNullOrWhiteSpace(topic.TriggerPayloadJson) ? "{}" : topic.TriggerPayloadJson,
            State = SubconsciousSessionState.Queued.ToString(),
            Priority = InferPriority(topic.TriggerEventType),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        try
        {
            dbContext.SubconsciousDebateSessions.Add(entity);
            EmitLifecycleEvent(
                MemoryEventTypes.SubconsciousDebateRequested,
                entity,
                new
                {
                    entity.DebateId,
                    entity.SessionId,
                    entity.TopicKey,
                    entity.TriggerEventType,
                    entity.TriggerEventId
                });
            await dbContext.SaveChangesAsync(cancellationToken);
            DebatesQueued.Add(1, new KeyValuePair<string, object?>("topic.key", entity.TopicKey));
        }
        catch (DbUpdateException ex) when (topic.TriggerEventId.HasValue)
        {
            dbContext.Entry(entity).State = EntityState.Detached;
            logger.LogDebug(
                ex,
                "Subconscious queue dedup hit. SessionId={SessionId} TopicKey={TopicKey} TriggerEventId={TriggerEventId}",
                normalizedSessionId,
                effectiveTopicKey,
                topic.TriggerEventId);
        }
    }

    public async Task ProcessDebateAsync(Guid debateId, CancellationToken cancellationToken = default)
    {
        if (!options.Enabled)
        {
            return;
        }

        using var activity = ActivitySource.StartActivity("subconscious.process", ActivityKind.Internal);
        activity?.SetTag("debate.id", debateId.ToString());

        var session = await dbContext.SubconsciousDebateSessions.FirstOrDefaultAsync(x => x.DebateId == debateId, cancellationToken);
        if (session is null)
        {
            return;
        }
        activity?.SetTag("session.id", session.SessionId);
        activity?.SetTag("topic.key", session.TopicKey);

        if (session.State is not (nameof(SubconsciousSessionState.Queued) or nameof(SubconsciousSessionState.Running)))
        {
            return;
        }

        var startedAt = DateTimeOffset.UtcNow;
        session.State = SubconsciousSessionState.Running.ToString();
        session.StartedAtUtc = startedAt;
        session.UpdatedAtUtc = startedAt;
        EmitLifecycleEvent(MemoryEventTypes.SubconsciousDebateStarted, session, new { session.DebateId, session.SessionId, session.TopicKey });
        DebatesStarted.Add(1, new KeyValuePair<string, object?>("topic.key", session.TopicKey));
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var debateResult = await RunDebateAsync(session, cancellationToken);
            await PersistTurnsAsync(session.DebateId, session.CompanionId, debateResult.Turns, cancellationToken);

            var validation = outcomeValidator.Validate(debateResult.OutcomeJson);
            var now = DateTimeOffset.UtcNow;
            var outcomeEntity = await dbContext.SubconsciousDebateOutcomes.FirstOrDefaultAsync(x => x.DebateId == session.DebateId, cancellationToken);
            if (outcomeEntity is null)
            {
                outcomeEntity = new SubconsciousDebateOutcomeEntity
                {
                    DebateId = session.DebateId,
                    CompanionId = session.CompanionId,
                    CreatedAtUtc = now
                };
                dbContext.SubconsciousDebateOutcomes.Add(outcomeEntity);
            }

            outcomeEntity.CompanionId = session.CompanionId;
            outcomeEntity.OutcomeJson = debateResult.OutcomeJson;
            outcomeEntity.OutcomeHash = ComputeHash(debateResult.OutcomeJson);
            outcomeEntity.ValidationStatus = validation.Status;
            outcomeEntity.UpdatedAtUtc = now;
            outcomeEntity.ApplyStatus = "Pending";
            outcomeEntity.ApplyError = validation.Error;

            if (!validation.IsValid || validation.Outcome is null)
            {
                outcomeEntity.ApplyStatus = "Deferred";
                session.State = SubconsciousSessionState.Completed.ToString();
                session.LastError = validation.Error ?? "Outcome validation failed.";
                var deferredAction = await ScheduleDeferredDebateFollowUpAsync(
                    session,
                    reason: "OutcomeValidationFailed",
                    detail: session.LastError,
                    cancellationToken);
                EmitLifecycleEvent(
                    MemoryEventTypes.SubconsciousOutcomeValidationFailed,
                    session,
                    new
                    {
                        session.DebateId,
                        session.SessionId,
                        Error = session.LastError,
                        deferredActionId = deferredAction?.ActionId
                    });
                EmitLifecycleEvent(
                    MemoryEventTypes.SubconsciousMemoryUpdateDeferred,
                    session,
                    new
                    {
                        session.DebateId,
                        session.SessionId,
                        reason = "OutcomeValidationFailed",
                        deferredActionId = deferredAction?.ActionId,
                        runAtUtc = deferredAction?.RunAtUtc
                    });
                DebatesCompleted.Add(1, new KeyValuePair<string, object?>("topic.key", session.TopicKey));
            }
            else if (validation.RequiresUserInput || groupChatManager.ShouldRequestUserInput(validation.Outcome))
            {
                var canAutoApproveHighConfidence = options.ApplyOutcome
                                                   && options.AutoApproveHighConfidenceRequiringUserInput
                                                   && validation.Outcome.FinalConfidence >= Math.Clamp(options.AutoApproveConfidenceThreshold, 0, 1);

                if (canAutoApproveHighConfidence)
                {
                    var applyReport = await outcomeApplier.ApplyAsync(session.DebateId, session.SessionId, validation.Outcome, cancellationToken);
                    outcomeEntity.ValidationStatus = "Valid";
                    outcomeEntity.ApplyStatus = "Applied";
                    outcomeEntity.ApplyError = applyReport.AnyApplied
                        ? null
                        : "Auto-approved by confidence threshold, but no updates were eligible for apply.";
                    outcomeEntity.AppliedAtUtc = now;
                    session.State = SubconsciousSessionState.Completed.ToString();
                    EmitLifecycleEvent(
                        MemoryEventTypes.SubconsciousMemoryUpdateApplied,
                        session,
                        new
                        {
                            session.DebateId,
                            session.SessionId,
                            session.TopicKey,
                            autoApprovedHighConfidence = true,
                            confidence = validation.Outcome.FinalConfidence,
                            threshold = options.AutoApproveConfidenceThreshold,
                            applyReport
                        });
                    DebatesCompleted.Add(1, new KeyValuePair<string, object?>("topic.key", session.TopicKey));
                }
                else
                {
                    session.State = SubconsciousSessionState.AwaitingUser.ToString();
                    outcomeEntity.ApplyStatus = "Pending";
                    EmitLifecycleEvent(
                        MemoryEventTypes.SubconsciousDebateAwaitingUserInput,
                        session,
                        new { session.DebateId, session.SessionId, validation.Outcome.UserQuestion, validation.Outcome.FinalConfidence });
                    DebatesAwaitingUser.Add(1, new KeyValuePair<string, object?>("topic.key", session.TopicKey));
                }
            }
            else if (options.ApplyOutcome)
            {
                var applyReport = await outcomeApplier.ApplyAsync(session.DebateId, session.SessionId, validation.Outcome, cancellationToken);
                outcomeEntity.ApplyStatus = "Applied";
                outcomeEntity.ApplyError = applyReport.AnyApplied ? null : "No updates were eligible for apply. See applyReport skips.";
                outcomeEntity.AppliedAtUtc = now;
                session.State = SubconsciousSessionState.Completed.ToString();
                EmitLifecycleEvent(
                    MemoryEventTypes.SubconsciousMemoryUpdateApplied,
                    session,
                    new { session.DebateId, session.SessionId, session.TopicKey, applyReport });
                DebatesCompleted.Add(1, new KeyValuePair<string, object?>("topic.key", session.TopicKey));
            }
            else
            {
                outcomeEntity.ApplyStatus = "Skipped";
                session.State = SubconsciousSessionState.Completed.ToString();
                EmitLifecycleEvent(
                    MemoryEventTypes.SubconsciousMemoryUpdateSkipped,
                    session,
                    new { session.DebateId, session.SessionId, Reason = "ApplyOutcome disabled." });
                DebatesCompleted.Add(1, new KeyValuePair<string, object?>("topic.key", session.TopicKey));
            }

            session.UpdatedAtUtc = now;
            if (session.State is nameof(SubconsciousSessionState.Completed) or nameof(SubconsciousSessionState.Failed))
            {
                session.CompletedAtUtc = now;
            }

            await UpsertMetricsAsync(session, validation.Outcome, debateResult.Turns.Count, startedAt, now, cancellationToken);
            DebateDurationMs.Record((now - startedAt).TotalMilliseconds, new KeyValuePair<string, object?>("topic.key", session.TopicKey));
            EmitLifecycleEvent(
                MemoryEventTypes.SubconsciousDebateConcluded,
                session,
                new { session.DebateId, session.SessionId, session.State, turnCount = debateResult.Turns.Count });
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            session.State = SubconsciousSessionState.Completed.ToString();
            session.LastError = ex.Message;
            var now = DateTimeOffset.UtcNow;
            session.CompletedAtUtc = now;
            session.UpdatedAtUtc = now;
            var deferredAction = await ScheduleDeferredDebateFollowUpAsync(
                session,
                reason: "ProcessingException",
                detail: ex.Message,
                cancellationToken);
            EmitLifecycleEvent(
                MemoryEventTypes.SubconsciousMemoryUpdateDeferred,
                session,
                new
                {
                    session.DebateId,
                    session.SessionId,
                    reason = "ProcessingException",
                    error = ex.Message,
                    deferredActionId = deferredAction?.ActionId,
                    runAtUtc = deferredAction?.RunAtUtc
                });
            EmitLifecycleEvent(
                MemoryEventTypes.SubconsciousDebateConcluded,
                session,
                new
                {
                    session.DebateId,
                    session.SessionId,
                    session.State,
                    Error = ex.Message,
                    deferredActionId = deferredAction?.ActionId
                });
            DebatesCompleted.Add(1, new KeyValuePair<string, object?>("topic.key", session.TopicKey));
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogWarning(ex, "Subconscious debate deferred after processing issue. DebateId={DebateId} SessionId={SessionId}", session.DebateId, session.SessionId);
        }
    }

    public async Task<bool> ApproveDebateAsync(Guid debateId, CancellationToken cancellationToken = default)
    {
        var session = await dbContext.SubconsciousDebateSessions.FirstOrDefaultAsync(x => x.DebateId == debateId, cancellationToken);
        var outcomeEntity = await dbContext.SubconsciousDebateOutcomes.FirstOrDefaultAsync(x => x.DebateId == debateId, cancellationToken);
        if (session is null || outcomeEntity is null)
        {
            return false;
        }

        var validation = outcomeValidator.Validate(outcomeEntity.OutcomeJson);
        if (!validation.IsValid || validation.Outcome is null)
        {
            var now = DateTimeOffset.UtcNow;
            outcomeEntity.ValidationStatus = validation.Status;
            outcomeEntity.ApplyStatus = "Deferred";
            outcomeEntity.ApplyError = validation.Error ?? "User approval requested, but outcome could not be validated.";
            outcomeEntity.UpdatedAtUtc = now;
            session.State = SubconsciousSessionState.Completed.ToString();
            session.CompletedAtUtc = now;
            session.UpdatedAtUtc = now;
            session.LastError = outcomeEntity.ApplyError;
            var deferredAction = await ScheduleDeferredDebateFollowUpAsync(
                session,
                reason: "ApproveValidationFailed",
                detail: outcomeEntity.ApplyError,
                cancellationToken);
            EmitLifecycleEvent(
                MemoryEventTypes.SubconsciousMemoryUpdateDeferred,
                session,
                new
                {
                    session.DebateId,
                    session.SessionId,
                    reason = "ApproveValidationFailed",
                    deferredActionId = deferredAction?.ActionId,
                    runAtUtc = deferredAction?.RunAtUtc
                });
            EmitLifecycleEvent(
                MemoryEventTypes.SubconsciousDebateConcluded,
                session,
                new
                {
                    session.DebateId,
                    session.SessionId,
                    session.State,
                    deferredActionId = deferredAction?.ActionId
                });
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        var applyReport = await outcomeApplier.ApplyAsync(debateId, session.SessionId, validation.Outcome, cancellationToken);
        outcomeEntity.ApplyStatus = "Applied";
        outcomeEntity.ValidationStatus = "Valid";
        outcomeEntity.ApplyError = applyReport.AnyApplied ? null : "No updates were eligible for apply. See applyReport skips.";
        outcomeEntity.AppliedAtUtc = DateTimeOffset.UtcNow;
        outcomeEntity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        session.State = SubconsciousSessionState.Completed.ToString();
        session.CompletedAtUtc = DateTimeOffset.UtcNow;
        session.UpdatedAtUtc = DateTimeOffset.UtcNow;
        EmitLifecycleEvent(MemoryEventTypes.SubconsciousMemoryUpdateApplied, session, new { session.DebateId, session.SessionId, approvedByUser = true, applyReport });
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RejectDebateAsync(Guid debateId, CancellationToken cancellationToken = default)
    {
        var session = await dbContext.SubconsciousDebateSessions.FirstOrDefaultAsync(x => x.DebateId == debateId, cancellationToken);
        var outcomeEntity = await dbContext.SubconsciousDebateOutcomes.FirstOrDefaultAsync(x => x.DebateId == debateId, cancellationToken);
        if (session is null || outcomeEntity is null)
        {
            return false;
        }

        outcomeEntity.ApplyStatus = "Skipped";
        outcomeEntity.ApplyError = "Rejected by user.";
        outcomeEntity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        session.State = SubconsciousSessionState.Completed.ToString();
        session.CompletedAtUtc = DateTimeOffset.UtcNow;
        session.UpdatedAtUtc = DateTimeOffset.UtcNow;
        EmitLifecycleEvent(MemoryEventTypes.SubconsciousMemoryUpdateSkipped, session, new { session.DebateId, session.SessionId, rejectedByUser = true });
        EmitLifecycleEvent(MemoryEventTypes.SubconsciousDebateConcluded, session, new { session.DebateId, session.SessionId, session.State, rejectedByUser = true });
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<ScheduledActionEntity?> ScheduleDeferredDebateFollowUpAsync(
        SubconsciousDebateSessionEntity session,
        string reason,
        string? detail,
        CancellationToken cancellationToken)
    {
        try
        {
            var runAtUtc = DateTimeOffset.UtcNow.AddMinutes(Math.Max(1, options.DeferredFollowUpDelayMinutes));
            var payload = JsonSerializer.Serialize(
                new
                {
                    priorDebateId = session.DebateId,
                    sessionId = session.SessionId,
                    topicKey = session.TopicKey,
                    reason,
                    detail = Truncate(detail, 600),
                    atUtc = DateTimeOffset.UtcNow
                });

            var inputJson = JsonSerializer.Serialize(
                new
                {
                    topicKey = session.TopicKey,
                    triggerEventType = "SubconsciousDeferredFollowUp",
                    triggerPayloadJson = payload
                });

            return await scheduledActionStore.ScheduleAsync(
                session.SessionId,
                actionType: "queue_subconscious_debate",
                inputJson: inputJson,
                runAtUtc: runAtUtc,
                maxAttempts: 1,
                cancellationToken: cancellationToken);
        }
        catch (Exception scheduleEx)
        {
            logger.LogWarning(
                scheduleEx,
                "Could not schedule deferred subconscious follow-up. DebateId={DebateId} SessionId={SessionId}",
                session.DebateId,
                session.SessionId);
            return null;
        }
    }

    private async Task<(List<SubconsciousTurnPayload> Turns, string OutcomeJson)> RunDebateAsync(
        SubconsciousDebateSessionEntity session,
        CancellationToken cancellationToken)
    {
        var kernel = kernelFactory.CreateChatKernel();
        var debatePrompt = await BuildDebatePromptAsync(session, cancellationToken);

        var curator = CreateAgent(kernel, "ContextCurator", "curator", CuratorInstructions);
        var skeptic = CreateAgent(kernel, "Skeptic", "skeptic", SkepticInstructions);
        var historian = CreateAgent(kernel, "Historian", "historian", HistorianInstructions);
        var strategist = CreateAgent(kernel, "Strategist", "strategist", StrategistInstructions);
        var synthesizer = CreateAgent(kernel, "Synthesizer", "synthesizer", SynthesizerInstructions);
        var agentsByRole = new Dictionary<string, ChatCompletionAgent>(StringComparer.OrdinalIgnoreCase)
        {
            ["curator"] = curator,
            ["skeptic"] = skeptic,
            ["historian"] = historian,
            ["strategist"] = strategist,
            ["synthesizer"] = synthesizer
        };
        var chat = new AgentGroupChat(curator, skeptic, historian, strategist, synthesizer);
        chat.AddChatMessage(new ChatMessageContent(AuthorRole.User, debatePrompt));

        var turns = new List<SubconsciousTurnPayload>();
        var turnNumber = 1;
        var unresolvedConflict = session.TriggerEventType is MemoryEventTypes.SemanticContradictionAdded or MemoryEventTypes.SemanticEvidenceAdded;
        string? lastRole = null;
        var cognitiveProfile = await ResolveCognitiveProfileAsync(session.SessionId, cancellationToken);
        var maxTurns = Math.Clamp(
            cognitiveProfile.Profile.Reflection.Debate.TurnCap,
            3,
            Math.Clamp(options.MaxDebateTurns, 3, 16));
        var convergenceDeltaMin = Math.Max(0.001, cognitiveProfile.Profile.Reflection.Debate.ConvergenceDeltaMin);

        while (turnNumber <= maxTurns)
        {
            var nextRole = groupChatManager.SelectNextAgent(
                turns.Select(x => new SubconsciousDebateTurnSignal(x.TurnNumber, x.Role, x.Message, x.Confidence)).ToArray(),
                unresolvedConflict,
                session.TriggerEventType,
                lastRole,
                maxTurns,
                cognitiveProfile.Profile);
            if (!agentsByRole.TryGetValue(nextRole, out var agent))
            {
                agent = synthesizer;
                nextRole = "synthesizer";
            }

            var emittedAny = false;
            await foreach (var message in chat.InvokeAsync(agent, cancellationToken))
            {
                var content = message.Content?.Trim();
                if (string.IsNullOrWhiteSpace(content))
                {
                    continue;
                }

                emittedAny = true;
                var role = ResolveRole(agent.Name);
                var confidence = TryExtractConfidence(content);
                turns.Add(new SubconsciousTurnPayload(turnNumber, message.AuthorName ?? agent.Name ?? "agent", role, content, confidence));
                DebateTurns.Add(1, new KeyValuePair<string, object?>("role", role));
                EmitLifecycleEvent(
                    MemoryEventTypes.SubconsciousDebateTurnCompleted,
                    session,
                    new { session.DebateId, session.SessionId, turnNumber, agent = message.AuthorName ?? agent.Name ?? "agent" });

                if (role is "skeptic" or "historian")
                {
                    unresolvedConflict = ContainsConflictSignal(content);
                }

                turnNumber += 1;
            }

            lastRole = nextRole;
            if (!emittedAny)
            {
                // If a role emits nothing, move to synthesis and terminate.
                if (!string.Equals(nextRole, "synthesizer", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                break;
            }

            if (string.Equals(nextRole, "synthesizer", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (groupChatManager.ShouldTerminate(
                    turns.Select(x => new SubconsciousDebateTurnSignal(x.TurnNumber, x.Role, x.Message, x.Confidence)).ToArray(),
                    isSynthesizerTurn: false,
                    convergenceDeltaMin))
            {
                lastRole = "strategist";
            }
        }

        if (!turns.Any(x => x.Role == "synthesizer"))
        {
            await foreach (var message in chat.InvokeAsync(synthesizer, cancellationToken))
            {
                var content = message.Content?.Trim();
                if (string.IsNullOrWhiteSpace(content))
                {
                    continue;
                }

                var confidence = TryExtractConfidence(content);
                turns.Add(new SubconsciousTurnPayload(turnNumber, message.AuthorName ?? synthesizer.Name ?? "agent", "synthesizer", content, confidence));
                DebateTurns.Add(1, new KeyValuePair<string, object?>("role", "synthesizer"));
                EmitLifecycleEvent(
                    MemoryEventTypes.SubconsciousDebateTurnCompleted,
                    session,
                    new { session.DebateId, session.SessionId, turnNumber, agent = message.AuthorName ?? synthesizer.Name ?? "agent" });
                turnNumber += 1;
            }
        }

        var synthCandidate = turns.LastOrDefault(x => x.Role == "synthesizer")?.Message
                             ?? turns.LastOrDefault()?.Message
                             ?? "{}";
        var normalizedOutcome = ExtractAndNormalizeOutcomeJson(groupChatManager.FilterResults(synthCandidate));
        return (turns, normalizedOutcome);
    }

    private async Task<string> BuildDebatePromptAsync(SubconsciousDebateSessionEntity session, CancellationToken cancellationToken)
    {
        var working = await workingMemoryStore.GetAsync(session.SessionId, cancellationToken);
        var episodic = await episodicMemoryRepository.QueryBySessionAsync(session.SessionId, take: Math.Clamp(options.WorkingContextTake, 5, 60), cancellationToken: cancellationToken);
        var semantic = await semanticMemoryRepository.QueryClaimsAsync(
            session.CompanionId,
            subject: $"session:{session.SessionId}",
            take: 20,
            cancellationToken: cancellationToken);
        var routines = await proceduralMemoryRepository.QueryRecentAsync(10, cancellationToken);
        var self = await selfModelRepository.GetAsync(cancellationToken);

        var workingText = string.Join('\n', working.Turns.TakeLast(options.WorkingContextTake).Select(x => $"- {x.CreatedAtUtc:HH:mm:ss} {x.Role}: {Truncate(x.Content, 200)}"));
        var episodicText = string.Join('\n', episodic.Take(12).Select(x => $"- {x.OccurredAt:yyyy-MM-dd HH:mm} {Truncate(x.What, 160)}"));
        var semanticText = string.Join('\n', semantic.Take(12).Select(x => $"- [{x.Confidence:F2}] {x.Subject}.{x.Predicate}={Truncate(x.Value, 140)}"));
        var routineText = string.Join('\n', routines.Take(8).Select(x => $"- {x.Trigger}: {Truncate(x.Outcome, 140)}"));
        var selfText = string.Join('\n', self.Preferences.Take(12).Select(x => $"- {x.Key}={Truncate(x.Value, 140)}"));

        return $"""
                Debate context:
                - SessionId: {session.SessionId}
                - TopicKey: {session.TopicKey}
                - TriggerEventType: {session.TriggerEventType}
                - TriggerPayloadJson: {session.TriggerPayloadJson}

                Working memory:
                {SafeBlock(workingText)}

                Episodic memory:
                {SafeBlock(episodicText)}

                Semantic memory:
                {SafeBlock(semanticText)}

                Procedural memory:
                {SafeBlock(routineText)}

                Self model:
                {SafeBlock(selfText)}

                Objective:
                Debate internal consistency and propose precise memory updates with confidence.
                Synthesizer must output JSON only and match schema exactly.
                """;
    }

    private static ChatCompletionAgent CreateAgent(Kernel kernel, string name, string role, string instructions)
        => new()
        {
            Name = name,
            Description = role,
            Instructions = instructions,
            Kernel = kernel
        };

    private async Task PersistTurnsAsync(
        Guid debateId,
        Guid companionId,
        IReadOnlyList<SubconsciousTurnPayload> turns,
        CancellationToken cancellationToken)
    {
        if (turns.Count == 0)
        {
            return;
        }

        foreach (var turn in turns)
        {
            dbContext.SubconsciousDebateTurns.Add(
                new SubconsciousDebateTurnEntity
                {
                    TurnId = Guid.NewGuid(),
                    DebateId = debateId,
                    CompanionId = companionId,
                    TurnNumber = turn.TurnNumber,
                    AgentName = turn.AgentName,
                    Role = turn.Role,
                    Message = turn.Message,
                    Confidence = turn.Confidence,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task UpsertMetricsAsync(
        SubconsciousDebateSessionEntity session,
        SubconsciousDebateOutcome? outcome,
        int turnCount,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.SubconsciousDebateMetrics.FirstOrDefaultAsync(x => x.DebateId == session.DebateId, cancellationToken);
        if (entity is null)
        {
            entity = new SubconsciousDebateMetricEntity
            {
                DebateId = session.DebateId,
                CompanionId = session.CompanionId
            };
            dbContext.SubconsciousDebateMetrics.Add(entity);
        }

        entity.CompanionId = session.CompanionId;
        entity.TurnCount = turnCount;
        entity.DurationMs = Math.Max(0, (int)(completedAt - startedAt).TotalMilliseconds);
        var confidenceFromTurns = turnCount == 0
            ? 0
            : dbContext.SubconsciousDebateTurns
                .AsNoTracking()
                .Where(x => x.DebateId == session.DebateId && x.Confidence.HasValue)
                .OrderByDescending(x => x.TurnNumber)
                .Select(x => x.Confidence!.Value)
                .FirstOrDefault();
        entity.ConvergenceScore = outcome?.FinalConfidence ?? confidenceFromTurns;
        entity.ContradictionsDetected = outcome?.Contradictions.Count ?? 0;
        entity.ClaimsProposed = (outcome?.ClaimsToCreate.Count ?? 0) + (outcome?.ClaimsToSupersede.Count ?? 0);
        entity.ClaimsApplied = session.State == SubconsciousSessionState.Completed.ToString() ? entity.ClaimsProposed : 0;
        entity.RequiresUserInput = outcome?.RequiresUserInput ?? false;
        entity.FinalConfidence = outcome?.FinalConfidence ?? 0;
        entity.CreatedAtUtc = completedAt;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<ResolvedCompanionCognitiveProfile> ResolveCognitiveProfileAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await cognitiveProfileResolver.ResolveBySessionIdAsync(sessionId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Failed to resolve cognitive profile for debate session. Using defaults.");
            var fallback = new CompanionCognitiveProfileDocument();
            return new ResolvedCompanionCognitiveProfile(
                Guid.Empty,
                Guid.Empty,
                0,
                fallback,
                new CompanionCognitiveRuntimePolicy(Guid.Empty, Guid.Empty, 0, fallback, new RuntimeLimits(120, 20, 8, 1)),
                IsFallback: true);
        }
    }

    private int ComputeDebounceSeconds(double triggerSensitivity)
    {
        var sensitivity = Math.Clamp(triggerSensitivity, 0, 1);
        var adaptive = options.DebateDebounceSeconds * (1.25 - (sensitivity * 0.75));
        return Math.Clamp((int)Math.Round(adaptive), 1, 60);
    }

    private void EmitLifecycleEvent(string eventType, SubconsciousDebateSessionEntity session, object payload)
    {
        outboxWriter.Enqueue(
            eventType,
            aggregateType: "SubconsciousDebate",
            aggregateId: session.DebateId.ToString("N"),
            payload: payload);
    }

    private static int InferPriority(string triggerEventType)
        => triggerEventType == MemoryEventTypes.SemanticContradictionAdded ? 100 : 50;

    private static string ResolveTopicKey(SubconsciousDebateTopic topic)
    {
        var requested = (topic.TopicKey ?? string.Empty).Trim();
        if (!GenericTopicKeys.Contains(requested))
        {
            return NormalizeTitle(requested);
        }

        var generated = TryGenerateTopicTitle(topic.TriggerEventType, topic.TriggerPayloadJson);
        if (!string.IsNullOrWhiteSpace(generated))
        {
            return generated;
        }

        return topic.TriggerEventType switch
        {
            MemoryEventTypes.SemanticContradictionAdded => "Resolve Contradiction",
            MemoryEventTypes.SelfPreferenceSet => "Refine Self Model",
            MemoryEventTypes.ProceduralRoutineUpserted => "Review Routine Update",
            MemoryEventTypes.SemanticClaimSuperseded => "Reconcile Superseded Claim",
            MemoryEventTypes.SemanticClaimCreated => "Review New Semantic Claim",
            MemoryEventTypes.EpisodicMemoryCreated => "Reflect On Recent Event",
            _ => "Context Refinement"
        };
    }

    private static string? TryGenerateTopicTitle(string triggerEventType, string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;
            switch (triggerEventType)
            {
                case MemoryEventTypes.SelfPreferenceSet:
                {
                    var key = TryReadString(root, "key");
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        return NormalizeTitle($"Update {ToFriendlyToken(key)}");
                    }
                    break;
                }
                case MemoryEventTypes.SemanticClaimCreated:
                case MemoryEventTypes.SemanticClaimSuperseded:
                {
                    var predicate = TryReadString(root, "predicate");
                    if (!string.IsNullOrWhiteSpace(predicate))
                    {
                        return NormalizeTitle($"Review {ToFriendlyToken(predicate)}");
                    }
                    break;
                }
                case MemoryEventTypes.ProceduralRoutineUpserted:
                {
                    var name = TryReadString(root, "name");
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        return NormalizeTitle($"Routine: {name}");
                    }

                    var trigger = TryReadString(root, "trigger");
                    if (!string.IsNullOrWhiteSpace(trigger))
                    {
                        return NormalizeTitle($"Routine Trigger: {trigger}");
                    }
                    break;
                }
                case MemoryEventTypes.EpisodicMemoryCreated:
                {
                    var what = TryReadString(root, "what");
                    if (!string.IsNullOrWhiteSpace(what))
                    {
                        return NormalizeTitle($"Reflect: {TrimWords(what, 7)}");
                    }
                    break;
                }
            }

            var fallback = TryReadString(root, "topic")
                           ?? TryReadString(root, "title")
                           ?? TryReadString(root, "name")
                           ?? TryReadString(root, "predicate")
                           ?? TryReadString(root, "key")
                           ?? TryReadString(root, "actionType");
            return string.IsNullOrWhiteSpace(fallback) ? null : NormalizeTitle(fallback);
        }
        catch
        {
            return null;
        }
    }

    private static string? TryReadString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var token))
        {
            return null;
        }

        return token.ValueKind switch
        {
            JsonValueKind.String => token.GetString()?.Trim(),
            JsonValueKind.Number => token.GetRawText(),
            _ => null
        };
    }

    private static string ToFriendlyToken(string raw)
    {
        var value = (raw ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return string.Empty;
        }

        value = value.Replace(':', ' ').Replace('.', ' ').Replace('_', ' ').Replace('-', ' ');
        return CollapseWhitespace(value);
    }

    private static string TrimWords(string text, int maxWords)
    {
        var cleaned = CollapseWhitespace(text);
        if (cleaned.Length == 0)
        {
            return cleaned;
        }

        var words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= maxWords)
        {
            return cleaned;
        }

        return $"{string.Join(' ', words.Take(maxWords))}...";
    }

    private static string NormalizeTitle(string raw)
    {
        var cleaned = CollapseWhitespace(raw);
        cleaned = cleaned.Trim('"', '\'', '.', ',', ';', ':');
        if (cleaned.Length == 0)
        {
            return "Context Refinement";
        }

        if (cleaned.Length > 64)
        {
            cleaned = $"{cleaned[..61].TrimEnd()}...";
        }

        var title = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(cleaned.ToLowerInvariant());
        return title;
    }

    private static string CollapseWhitespace(string value)
        => Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();

    private static string ResolveRole(string? agentName)
    {
        if (string.IsNullOrWhiteSpace(agentName))
        {
            return "agent";
        }

        var normalized = agentName.ToLowerInvariant();
        return normalized.Contains("synth") ? "synthesizer"
            : normalized.Contains("skeptic") ? "skeptic"
            : normalized.Contains("historian") ? "historian"
            : normalized.Contains("strateg") ? "strategist"
            : normalized.Contains("curator") ? "curator"
            : "agent";
    }

    private static string Truncate(string? value, int max)
    {
        var text = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length <= max ? text : $"{text[..max]}...";
    }

    private static string SafeBlock(string text) => string.IsNullOrWhiteSpace(text) ? "- none" : text;

    private static string ComputeHash(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private static string ExtractAndNormalizeOutcomeJson(string raw)
    {
        var text = StripMarkdownFences(raw).Trim();
        var json = ExtractJsonObject(text);
        if (json == "{}")
        {
            return BuildFallbackOutcomeJson(text);
        }

        if (TryDeserializeOutcome(json, out var parsed))
        {
            return JsonSerializer.Serialize(parsed, SubconsciousDebateOutcome.JsonOptions);
        }

        var repaired = RepairLooseJson(json);
        if (TryDeserializeOutcome(repaired, out parsed))
        {
            return JsonSerializer.Serialize(parsed, SubconsciousDebateOutcome.JsonOptions);
        }

        return BuildFallbackOutcomeJson(text);
    }

    private static string ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            return text[start..(end + 1)];
        }
        return "{}";
    }

    private static string BuildFallbackOutcomeJson(string rawMessage)
        => JsonSerializer.Serialize(
            new SubconsciousDebateOutcome(
                DecisionType: "needs_user_input",
                FinalConfidence: 0.35,
                ReasoningSummary: Truncate(rawMessage, 500),
                EvidenceRefs: [],
                ClaimsToCreate: [],
                ClaimsToSupersede: [],
                Contradictions: [],
                ProceduralUpdates: [],
                SelfUpdates: [],
                RequiresUserInput: true,
                UserQuestion: "I could not produce a valid structured subconscious outcome. Please review manually."),
            SubconsciousDebateOutcome.JsonOptions);

    private static bool TryDeserializeOutcome(string json, out SubconsciousDebateOutcome? outcome)
    {
        try
        {
            outcome = JsonSerializer.Deserialize<SubconsciousDebateOutcome>(json, SubconsciousDebateOutcome.JsonOptions);
            return outcome is not null;
        }
        catch
        {
            outcome = null;
            return false;
        }
    }

    private static string RepairLooseJson(string json)
    {
        var repaired = json;
        repaired = Regex.Replace(repaired, @":\s*\[\s*\.{3}\s*\]", ": []", RegexOptions.CultureInvariant);
        repaired = Regex.Replace(repaired, @":\s*\{\s*\.{3}\s*\}", ": {}", RegexOptions.CultureInvariant);
        repaired = Regex.Replace(repaired, @"\[\s*\.{3}\s*,?", "[]", RegexOptions.CultureInvariant);
        repaired = Regex.Replace(repaired, @",\s*(\]|\})", "$1", RegexOptions.CultureInvariant);
        return repaired;
    }

    private static string StripMarkdownFences(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var normalized = trimmed
            .Replace("```json", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("```", string.Empty, StringComparison.Ordinal)
            .Trim();
        return normalized;
    }

    private static bool ContainsConflictSignal(string content)
    {
        var normalized = content.ToLowerInvariant();
        return normalized.Contains("conflict", StringComparison.Ordinal)
               || normalized.Contains("contradict", StringComparison.Ordinal)
               || normalized.Contains("inconsistent", StringComparison.Ordinal);
    }

    private static double? TryExtractConfidence(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var regex = new Regex(@"confidence[^0-9]*([01](?:\.\d+)?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var match = regex.Match(content);
        if (match.Success && double.TryParse(match.Groups[1].Value, out var parsed))
        {
            return Math.Clamp(parsed, 0, 1);
        }

        var json = ExtractJsonObject(content);
        if (json == "{}")
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("finalConfidence", out var finalConfidence))
            {
                if (finalConfidence.ValueKind == JsonValueKind.Number && finalConfidence.TryGetDouble(out var value))
                {
                    return Math.Clamp(value, 0, 1);
                }
            }
        }
        catch
        {
            // ignore parse failures
        }

        return null;
    }

    private const string CuratorInstructions = """
        You are ContextCurator. Summarize relevant context, timeline, and unresolved questions.
        Be concise and evidence-backed. Do not output JSON.
        """;

    private const string SkepticInstructions = """
        You are Skeptic. Challenge weak assumptions and identify contradictions or missing evidence.
        Be strict and concrete. Do not output JSON.
        """;

    private const string HistorianInstructions = """
        You are Historian. Check consistency against prior memory and temporal order.
        Flag supersession opportunities. Do not output JSON.
        """;

    private const string StrategistInstructions = """
        You are Strategist. Propose actionable procedural improvements and decision paths.
        Keep proposals realistic and bounded. Do not output JSON.
        """;

    private const string SynthesizerInstructions = """
        You are Synthesizer. Output STRICT JSON only with this schema:
        {
          \"decisionType\": \"no_change|refine|resolve_conflict|promote_routine|identity_update|needs_user_input\",
          \"finalConfidence\": 0.0,
          \"reasoningSummary\": \"string\",
          \"evidenceRefs\": [{\"source\":\"working|episodic|semantic|procedural|self\",\"referenceId\":\"string\",\"weight\":0.0}],
          \"claimsToCreate\": [{\"subject\":\"string\",\"predicate\":\"string\",\"value\":\"string\",\"confidence\":0.0,\"scope\":\"global|session\"}],
          \"claimsToSupersede\": [{\"claimId\":\"uuid\",\"replacement\":{\"subject\":\"string\",\"predicate\":\"string\",\"value\":\"string\",\"confidence\":0.0,\"scope\":\"global|session\"}}],
          \"contradictions\": [{\"claimAId\":\"uuid\",\"claimBId\":\"uuid\",\"severity\":\"low|medium|high\",\"status\":\"detected|resolved|needs_review\"}],
          \"proceduralUpdates\": [{\"routineId\":\"uuid|null\",\"trigger\":\"string\",\"name\":\"string\",\"steps\":[\"string\"],\"outcome\":\"string\"}],
          \"selfUpdates\": [{\"key\":\"string\",\"value\":\"string\",\"confidence\":0.0,\"requiresConfirmation\":true}],
          \"requiresUserInput\": false,
          \"userQuestion\": \"string|null\"
        }
        No markdown. No explanations outside JSON.
        Do NOT emit placeholders like [...], {...}, TODO, or comments.
        If a list is unknown, use [].
        """;

    private sealed record SubconsciousTurnPayload(int TurnNumber, string AgentName, string Role, string Message, double? Confidence);
}
