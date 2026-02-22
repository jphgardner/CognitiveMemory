# Subconscious Internal Debate Blueprint (Full Feature Spec)

## 1. Purpose
Build a production-grade subconscious system that runs internal multi-agent debates using Semantic Kernel Group Chat Orchestration, then applies validated outcomes to CognitiveMemory layers through controlled, auditable memory updates.

This blueprint defines:
- architecture
- runtime flows
- data model
- event contracts
- orchestration manager behavior
- API/UI surfaces
- safety policies
- observability
- rollout strategy
- test requirements

---

## 2. Product Outcomes

### 2.1 Primary outcomes
- Continuously improve memory quality in the background.
- Resolve contradictions proactively.
- Evolve identity/procedural knowledge with confidence gating.
- Preserve complete traceability from trigger event to final memory writes.

### 2.2 Non-goals
- Replacing direct chat responses with debate output.
- Allowing unconstrained autonomous writes.
- Persisting free-form chain-of-thought into long-term memory.

---

## 3. High-Level Architecture

## 3.1 Core components
- `SubconsciousDebateSchedulerConsumer` (reactive event consumer)
- `SubconsciousDebateWorker` (hosted service, executes queued debates)
- `SubconsciousDebateService` (orchestration + runtime integration)
- `SubconsciousGroupChatManager` (custom SK manager behavior)
- `SubconsciousOutcomeValidator` (strict schema and safety gates)
- `SubconsciousOutcomeApplier` (writes to repositories/tools)
- `SubconsciousEventPublisher` (outbox events for debate lifecycle)

## 3.2 Semantic Kernel orchestration
- Use `GroupChatOrchestration` with multiple agents.
- Use custom `GroupChatManager` overrides:
  - `SelectNextAgent`
  - `ShouldTerminate`
  - `ShouldRequestUserInput`
  - `FilterResults`
- Runtime:
  - Phase 1: `InProcessRuntime`
  - Phase 2+: optional distributed executor through RabbitMQ-triggered jobs

## 3.3 Integration with existing event platform
- Triggered by existing outbox-driven events.
- Debate lifecycle emits new outbox events.
- Existing dead-letter + replay mechanisms apply to debate events too.

---

## 4. Debate Agent Topology

## 4.1 Required agents
- `ContextCuratorAgent`
  - gathers latest relevant context from working/episodic/semantic/procedural/self
  - frames debate objective
- `SkepticAgent`
  - challenges unsupported assumptions
  - seeks contradictions and weak evidence
- `HistorianAgent`
  - checks temporal consistency and supersession history
  - prioritizes stable long-term facts
- `StrategistAgent`
  - proposes practical routines/actions from evidence
  - estimates forward utility and risk
- `SynthesizerAgent`
  - produces strict structured final outcome
  - no free-form output beyond approved schema

## 4.2 Optional specialized agents
- `SafetyAgent` (high-risk write policy enforcement)
- `IdentityGuardianAgent` (identity field hardening)
- `TimeAnchorAgent` (absolute-date normalization)

---

## 5. Debate Triggers and Debounce

## 5.1 Trigger events (must support)
- `EpisodicMemoryCreated`
- `SemanticClaimCreated`
- `SemanticContradictionAdded`
- `SemanticEvidenceAdded`
- `SemanticClaimSuperseded`
- `ProceduralRoutineUpserted`
- `SelfPreferenceSet`

## 5.2 Debounce rules
- Max one active debate per `sessionId + topicKey`.
- Coalesce triggers within `DebateDebounceSeconds` (default 10s).
- Drop duplicate trigger replay by idempotency key:
  - `TriggerEventId + DebateTopic + SessionId`

---

## 6. Data Model (EF + PostgreSQL)

## 6.1 `SubconsciousDebateSessions`
- `DebateId` (PK, uuid)
- `SessionId` (varchar 128, indexed)
- `TopicKey` (varchar 128, indexed)
- `TriggerEventId` (uuid, nullable, indexed)
- `TriggerEventType` (varchar 128)
- `State` (varchar 32)  
  values: `Queued|Running|AwaitingUser|Completed|Failed|Canceled`
- `Priority` (int)
- `StartedAtUtc` (timestamptz, nullable)
- `CompletedAtUtc` (timestamptz, nullable)
- `LastError` (text, nullable)
- `CreatedAtUtc` (timestamptz)
- `UpdatedAtUtc` (timestamptz)

## 6.2 `SubconsciousDebateTurns`
- `TurnId` (PK, uuid)
- `DebateId` (FK -> `SubconsciousDebateSessions`)
- `TurnNumber` (int)
- `AgentName` (varchar 128)
- `Role` (varchar 64)  
  values: `curator|skeptic|historian|strategist|synthesizer|manager`
- `Message` (text)
- `StructuredPayloadJson` (text, nullable)
- `Confidence` (double, nullable)
- `CreatedAtUtc` (timestamptz)
- unique index: `(DebateId, TurnNumber)`

## 6.3 `SubconsciousDebateOutcomes`
- `DebateId` (PK/FK -> `SubconsciousDebateSessions`)
- `OutcomeJson` (text)
- `OutcomeHash` (varchar 128, indexed)
- `ValidationStatus` (varchar 32)  
  values: `Valid|Rejected|NeedsUserConfirmation`
- `ApplyStatus` (varchar 32)  
  values: `Pending|Applied|Skipped|Failed`
- `ApplyError` (text, nullable)
- `AppliedAtUtc` (timestamptz, nullable)
- `CreatedAtUtc` (timestamptz)
- `UpdatedAtUtc` (timestamptz)

## 6.4 `SubconsciousDebateMetrics` (projection table)
- `DebateId` (PK)
- `TurnCount` (int)
- `DurationMs` (int)
- `ConvergenceScore` (double)
- `ContradictionsDetected` (int)
- `ClaimsProposed` (int)
- `ClaimsApplied` (int)
- `RequiresUserInput` (bool)
- `FinalConfidence` (double)
- `CreatedAtUtc` (timestamptz)

---

## 7. Event Contracts

## 7.1 New event types
- `SubconsciousDebateRequested`
- `SubconsciousDebateStarted`
- `SubconsciousDebateTurnCompleted`
- `SubconsciousDebateAwaitingUserInput`
- `SubconsciousDebateConcluded`
- `SubconsciousOutcomeValidationFailed`
- `SubconsciousMemoryUpdateApplied`
- `SubconsciousMemoryUpdateSkipped`
- `SubconsciousDebateFailed`

## 7.2 Envelope payload conventions
- include:
  - `debateId`
  - `sessionId`
  - `topicKey`
  - `triggerEventId`
  - `triggerEventType`
  - `occurredAtUtc`
  - compact summary fields only (full details remain in DB tables)

---

## 8. Orchestration Lifecycle

## 8.1 State machine
1. `Queued`
2. `Running`
3. `AwaitingUser` (optional)
4. `Completed` or `Failed` or `Canceled`

## 8.2 Execution flow
1. Scheduler receives trigger event.
2. Debounce/idempotency check.
3. Create `SubconsciousDebateSession` row (`Queued`).
4. Worker picks queue item and marks `Running`.
5. Build SK agents and `GroupChatOrchestration`.
6. Run debate turns under manager policy.
7. Persist turns as they are produced.
8. Manager emits structured final result.
9. Validate outcome JSON.
10. If valid and policy-safe, apply memory updates.
11. Persist outcome/apply status.
12. Emit lifecycle events.

---

## 9. Custom Manager Policy (exact behavior)

## 9.1 `SelectNextAgent`
- Inputs:
  - turn history
  - unresolved contradiction count
  - confidence trend
  - open policy flags
- Selection policy:
  - first turn always `ContextCuratorAgent`
  - if contradictions unresolved -> `SkepticAgent` then `HistorianAgent`
  - if contradictions low and actionability low -> `StrategistAgent`
  - last turn must be `SynthesizerAgent`
- Hard limit:
  - `MaxDebateTurns` default 8

## 9.2 `ShouldTerminate`
Terminate when any true:
- turn count >= `MaxDebateTurns`
- synthesizer produced valid outcome and confidence >= threshold
- confidence delta across last N turns < `ConvergenceDeltaMin`
- manager detects repetitive loop

## 9.3 `ShouldRequestUserInput`
Return `true` when outcome contains:
- identity overwrite on protected keys (`identity.name`, `identity.birth_datetime`, `identity.origin`, `identity.role`)
- destructive supersession with confidence gap < threshold
- mutually exclusive claims both high confidence

## 9.4 `FilterResults`
- Strip non-structured/free-form output.
- Return strict JSON only (schema below).
- Include compact human summary + machine payload.

---

## 10. Outcome JSON Schema (strict)

```json
{
  "decisionType": "no_change|refine|resolve_conflict|promote_routine|identity_update|needs_user_input",
  "finalConfidence": 0.0,
  "reasoningSummary": "string",
  "evidenceRefs": [
    {
      "source": "working|episodic|semantic|procedural|self",
      "referenceId": "string",
      "weight": 0.0
    }
  ],
  "claimsToCreate": [
    {
      "subject": "string",
      "predicate": "string",
      "value": "string",
      "confidence": 0.0,
      "scope": "global|session"
    }
  ],
  "claimsToSupersede": [
    {
      "claimId": "uuid",
      "replacement": {
        "subject": "string",
        "predicate": "string",
        "value": "string",
        "confidence": 0.0,
        "scope": "global|session"
      }
    }
  ],
  "contradictions": [
    {
      "claimAId": "uuid",
      "claimBId": "uuid",
      "severity": "low|medium|high",
      "status": "detected|resolved|needs_review"
    }
  ],
  "proceduralUpdates": [
    {
      "routineId": "uuid|null",
      "trigger": "string",
      "name": "string",
      "steps": ["string"],
      "outcome": "string"
    }
  ],
  "selfUpdates": [
    {
      "key": "string",
      "value": "string",
      "confidence": 0.0,
      "requiresConfirmation": true
    }
  ],
  "requiresUserInput": false,
  "userQuestion": "string|null"
}
```

Schema must be validated before apply. Unknown fields rejected.

---

## 11. Safety, Trust, and Memory Write Policy

## 11.1 General safety gates
- Reject any outcome with:
  - malformed schema
  - unsupported layer writes
  - invalid confidence ranges
  - missing evidenceRefs for non-trivial writes

## 11.2 Confidence thresholds
- semantic create: `>= 0.65`
- semantic supersede: replacement must exceed current by `>= 0.08`
- self updates: `>= 0.75` + confirmation for protected keys
- procedural updates: requires at least one evidence ref from episodic/working

## 11.3 Write restrictions
- never persist debate chain-of-thought as memory facts
- never overwrite protected identity keys without policy check
- never apply conflicting high-confidence claim pair in same apply batch

## 11.4 Human-in-the-loop
- when `requiresUserInput = true`:
  - session state -> `AwaitingUser`
  - UI/API expose pending question
  - applier pauses all sensitive writes

---

## 12. API Surface

## 12.1 Internal endpoints
- `GET /api/subconscious/debates/{sessionId}?take=...`
- `GET /api/subconscious/debates/{debateId}`
- `GET /api/subconscious/debates/{debateId}/turns`
- `GET /api/subconscious/debates/{debateId}/outcome`
- `POST /api/subconscious/debates/{debateId}/approve`
- `POST /api/subconscious/debates/{debateId}/reject`
- `POST /api/subconscious/debates/run-once` (ops/debug)

## 12.2 SSE streams
- `GET /api/subconscious/debates/stream?sessionId=...`
  - events:
    - `debate-started`
    - `debate-turn`
    - `debate-awaiting-user`
    - `debate-completed`
    - `debate-failed`

---

## 13. Frontend Features (all required)

## 13.1 Live subconscious panel
- right-side panel in Chat Studio:
  - active debate status
  - current speaking internal agent
  - turn-by-turn stream
  - outcome/apply status badges

## 13.2 Debate timeline
- per assistant reply:
  - linked trigger event
  - debate id
  - turn count/duration/confidence
  - applied memory updates

## 13.3 Human confirmation UX
- modal/card when debate requires user decision
- approve/reject buttons wired to API endpoints

## 13.4 Ops/analytics views
- debate throughput
- convergence/failure rates
- average duration
- policy-blocked write counts

---

## 14. Configuration Contract

Add `SubconsciousDebate` config section:

```json
{
  "SubconsciousDebate": {
    "Enabled": true,
    "MaxConcurrentDebates": 4,
    "MaxDebateTurns": 8,
    "DebateDebounceSeconds": 10,
    "WorkingContextTake": 20,
    "WorkingMemoryStaleMinutes": 30,
    "ConvergenceDeltaMin": 0.02,
    "TerminateConfidenceThreshold": 0.78,
    "RequireHumanApprovalForProtectedIdentity": true,
    "ProtectedIdentityKeys": [
      "identity.name",
      "identity.birth_datetime",
      "identity.origin",
      "identity.role"
    ]
  }
}
```

---

## 15. Dependency and Package Requirements

## 15.1 Required packages
- `Microsoft.SemanticKernel.Agents.Core` (already in central versions)

## 15.2 Runtime compatibility
- keep all SK packages on aligned versions
- isolate orchestration logic behind feature flag due experimental surface

---

## 16. Implementation Work Breakdown

## 16.1 Phase A: Foundation
- add domain contracts/interfaces:
  - `ISubconsciousDebateService`
  - `ISubconsciousOutcomeValidator`
  - `ISubconsciousOutcomeApplier`
- add options class:
  - `SubconsciousDebateOptions`
- add events constants in `MemoryEventTypes` (or dedicated `SubconsciousEventTypes`)

## 16.2 Phase B: Persistence
- add entities + DbSets + model config:
  - `SubconsciousDebateSessionEntity`
  - `SubconsciousDebateTurnEntity`
  - `SubconsciousDebateOutcomeEntity`
  - `SubconsciousDebateMetricEntity`
- add migration

## 16.3 Phase C: Scheduler and Worker
- `SubconsciousDebateSchedulerConsumer` (reactive trigger consumer)
- `SubconsciousDebateWorker` (hosted service)
- idempotency checks on queue insert + execution claim

## 16.4 Phase D: SK Orchestration
- create agent prompts
- implement `SubconsciousGroupChatManager`
- integrate `GroupChatOrchestration`
- persist turn stream during run

## 16.5 Phase E: Outcome Validation + Apply
- implement strict JSON schema parser/validator
- apply writes via existing repositories:
  - semantic claims/evidence/contradictions
  - procedural routines
  - self preferences
- emit `SubconsciousMemoryUpdateApplied|Skipped`

## 16.6 Phase F: API + UI
- add debate query/control endpoints
- add debate SSE stream
- frontend live panel and approval actions

## 16.7 Phase G: Observability + Hardening
- metrics, traces, logs
- dead-letter integration for debate failures
- replay tooling for failed debates

---

## 17. Observability Spec

## 17.1 Metrics (OpenTelemetry)
- `subconscious.debate.started` counter
- `subconscious.debate.completed` counter
- `subconscious.debate.failed` counter
- `subconscious.debate.duration.ms` histogram
- `subconscious.debate.turn.count` histogram
- `subconscious.debate.convergence.score` histogram
- `subconscious.outcome.apply.success` counter
- `subconscious.outcome.apply.failure` counter
- `subconscious.outcome.human_required` counter

## 17.2 Tracing
- root span per debate: `SubconsciousDebate.Run`
- child spans:
  - `SubconsciousDebate.AgentTurn`
  - `SubconsciousDebate.ValidateOutcome`
  - `SubconsciousDebate.ApplyOutcome`
- tags:
  - `debate.id`, `session.id`, `topic.key`, `trigger.event.type`, `turn.count`

## 17.3 Structured logging
- required keys:
  - `DebateId`, `SessionId`, `TopicKey`, `State`, `AgentName`, `TurnNumber`, `ValidationStatus`, `ApplyStatus`

---

## 18. Reliability and Concurrency

## 18.1 Execution controls
- global concurrency cap: `MaxConcurrentDebates`
- per-session single active debate per `topicKey`
- cancellation support for stale queued debates

## 18.2 Retry strategy
- worker retries transient orchestration failures up to `MaxRetries`
- terminal failures emit `SubconsciousDebateFailed` and park outcome

## 18.3 Dead-letter strategy
- failed debate lifecycle events use existing outbox dead-letter
- optional replay job:
  - `SubconsciousDebateReplayWorker`

---

## 19. Security and Compliance

- Never persist sensitive raw prompts unless explicitly configured.
- Redact PII in debate turn logs where possible.
- Separate internal debate transcript retention policy from core memory tables.
- Expose only filtered transcript to frontend (no raw tool secrets/system prompts).

---

## 20. Test Plan (must-have)

## 20.1 Unit tests
- manager selection logic
- termination logic
- user-input gate logic
- schema validation pass/fail matrix
- confidence threshold enforcement

## 20.2 Integration tests
- trigger event -> queued debate -> completed outcome -> memory writes
- trigger event -> policy block -> awaiting user
- trigger event -> orchestration failure -> failed state + event emission

## 20.3 API tests
- debate list/detail endpoints
- SSE stream event sequence
- approve/reject flows

## 20.4 Load tests
- concurrent trigger storms on same session/topic
- debounce correctness and no duplicate outcomes

---

## 21. Rollout Plan

## 21.1 Stage 1 (dark launch)
- enable writes disabled (`ApplyOutcome=false`)
- collect metrics + logs only

## 21.2 Stage 2 (safe write mode)
- enable only procedural + low-risk semantic updates
- keep protected identity updates behind approval

## 21.3 Stage 3 (full mode)
- enable full validated apply paths
- monitor SLA and failure budget

## 21.4 Rollback
- set `SubconsciousDebate.Enabled=false`
- workers stop; no debate execution
- existing chat and memory systems continue unaffected

---

## 22. Concrete File/Code Targets

Add/modify (suggested):
- `src/CognitiveMemory.Infrastructure/Subconscious/*`
  - `SubconsciousDebateService.cs`
  - `SubconsciousGroupChatManager.cs`
  - `SubconsciousOutcomeValidator.cs`
  - `SubconsciousOutcomeApplier.cs`
  - `SubconsciousDebateOptions.cs`
- `src/CognitiveMemory.Infrastructure/Reactive/SubconsciousDebateSchedulerConsumer.cs`
- `src/CognitiveMemory.Infrastructure/Background/SubconsciousDebateWorker.cs`
- `src/CognitiveMemory.Infrastructure/Persistence/Entities/*Subconscious*.cs`
- `src/CognitiveMemory.Infrastructure/Persistence/MemoryDbContext.cs`
- `src/CognitiveMemory.Infrastructure/Persistence/Migrations/*AddSubconsciousDebate*.cs`
- `src/CognitiveMemory.Infrastructure/DependencyInjection.cs`
- `src/CognitiveMemory.Api/Endpoints/SubconsciousDebateEndpoints.cs`
- `frontend/cognitive-memory-chat/src/app/*` (debate panel + SSE integration)
- `src/CognitiveMemory.Api/appsettings*.json` (new options)

---

## 23. Acceptance Criteria (Definition of Done)

- debate sessions are created automatically from triggers with debounce/idempotency.
- manager-driven multi-agent debate runs with bounded turns and convergence.
- strict structured outcome is validated before apply.
- memory updates are applied safely and auditable to `DebateId`.
- protected identity updates require explicit user approval.
- live debate state is visible in frontend via SSE.
- metrics/traces/logs provide full operational visibility.
- system can be disabled with a single feature flag without impacting chat runtime.
