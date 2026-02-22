# CognitiveMemory Codebase: Full System Map and Current Status
Generated: 2026-02-22  
Source: direct code inspection + local build/test run in this workspace.

## 1. What this product is
CognitiveMemory is a memory-centric conversational platform with:
- session chat with tool-calling,
- multi-layer memory persistence,
- event-driven cognitive processing,
- subconscious internal debate orchestration,
- scheduled action execution,
- operations UI with live streams for chat/event/debate/action state.

It is built as a .NET 10 multi-project solution with an Angular frontend, orchestrated locally via Aspire.

## 2. Repository structure
- `CognitiveMemory.AppHost`: Aspire distributed app orchestration (Postgres/pgAdmin, Redis, RabbitMQ, API, frontend, YARP gateway).
- `CognitiveMemory.ServiceDefaults`: shared service defaults, telemetry, resilience.
- `src/CognitiveMemory.Domain`: core memory domain models/enums.
- `src/CognitiveMemory.Application`: use-case services and abstractions.
- `src/CognitiveMemory.Infrastructure`: persistence, repositories, SK gateways/plugins, eventing, workers, subconscious system.
- `src/CognitiveMemory.Api`: minimal API transport + hosted workers + middleware.
- `frontend/cognitive-memory-chat`: Angular enterprise console.
- `tests/*`: API and application tests.

## 3. Runtime topology
Primary local entrypoint: `dotnet run --project CognitiveMemory.AppHost/CognitiveMemory.AppHost.csproj`.

Runtime wiring from `CognitiveMemory.AppHost/Program.cs`:
- Postgres image: `pgvector/pgvector:pg17`, host port `5432`, bind mount `data/db`.
- pgAdmin host port `5050`.
- Redis resource `cache`.
- RabbitMQ image `rabbitmq:3.13-management`, ports `5672` (AMQP) and `15672` (UI), bind mount `data/rabbitmq`.
- API project `api` references `memorydb` and `cache`, waits for Postgres/Redis/RabbitMQ.
- YARP gateway on host port `8080` routes:
  - `/api/**` -> API,
  - `/v1/**` -> API,
  - everything else -> frontend.

## 4. Memory model (implemented)
Memory layers:
- `working`: short-term session context (Redis).
- `episodic`: timestamped events (Postgres).
- `semantic`: claims/facts with confidence/evidence/contradictions/supersession (Postgres).
- `procedural`: routines (trigger, steps, outcome) (Postgres).
- `self`: self-model preferences/identity keys (Postgres).

Core domain types include:
- `EpisodicMemoryEvent`
- `SemanticClaim`, `ClaimEvidence`, `ClaimContradiction`, `SemanticClaimStatus`
- `ProceduralRoutine`
- `SelfPreference`, `SelfModelSnapshot`
- `ToolInvocationAudit`

## 5. Persistence schema status
`MemoryDbContext` currently maps:
- `EpisodicMemoryEvents`
- `SemanticClaims`
- `SemanticClaimEmbeddings` (with pgvector migration support)
- `ClaimEvidence`
- `ClaimContradictions`
- `ConsolidationPromotions`
- `ToolInvocationAudits`
- `ProceduralRoutines`
- `SelfPreferences`
- `OutboxMessages`
- `ScheduledActions`
- `EventConsumerCheckpoints`
- `ConflictEscalationAlerts`
- `UserProfileProjections`
- `ProceduralRoutineMetrics`
- `SubconsciousDebateSessions`
- `SubconsciousDebateTurns`
- `SubconsciousDebateOutcomes`
- `SubconsciousDebateMetrics`

Migrations present through:
- `20260222045230_AddScheduledActions` (latest in repo).

## 6. LLM architecture and model split
`SemanticKernelFactory` supports per-role model/provider split:
- Main chat model (`SemanticKernel.Provider`, `ChatModelId`) for primary assistant responses.
- Mini/routing model (`ClaimExtractionProvider`, `ClaimExtractionModelId`) for extraction/routing tasks.

Current `appsettings.Development.json` split:
- Main chat: OpenAI `gpt-5.2`.
- Loop model: `gpt-5-mini`.
- Claim extraction/routing: Ollama `llama3.2:3b`.

Current base `appsettings.json` (non-dev defaults):
- Provider: Ollama for chat + extraction.

## 7. Chat and tool behavior
`ChatService` injects a large system prompt and runtime prompt with:
- working-memory context injection,
- mandatory working-memory refresh when stale,
- explicit tool list and tool policy.

Memory tools currently exposed (`MemoryToolsPlugin`):
- `store_memory`
- `retrieve_memory`
- `get_current_time`
- `schedule_action`
- `list_scheduled_actions`
- `cancel_scheduled_action`

Notable behavior implemented:
- tool invocation auditing in `ToolInvocationAudits`.
- `retrieve_memory` serial layer retrieval to avoid EF DbContext concurrency overlap.
- `store_memory` supports multi-route results.
- routing mini model can use read-only routing-time tools.
- scheduler tools are available to the chat model.

## 8. Event-driven architecture
Outbox pattern implemented with durable events:
- writes emit events via `IOutboxWriter`,
- dispatcher publishes events (`OutboxDispatcherWorker`),
- transport can be in-process or RabbitMQ (`IOutboxPublisher` selection by config),
- idempotency via `EventConsumerCheckpoints` and `OutboxEventConsumerDispatcher`.

Event consumers include:
- consolidation/reactive reasoning/identity evolution/truth maintenance,
- analytics/SLA/confidence recalculation/conflict escalation/profile projection/routine effectiveness,
- subconscious debate scheduling.

Event types include memory, subconscious lifecycle, and scheduled-action lifecycle (`MemoryEventTypes`).

## 9. Subconscious debate system
Implemented components:
- debate queueing and processing service (`SubconsciousDebateService`),
- custom manager (`SubconsciousGroupChatManager`),
- strict outcome validation (`SubconsciousOutcomeValidator`),
- apply/preview pipeline (`SubconsciousOutcomeApplier`),
- worker (`SubconsciousDebateWorker`),
- reactive scheduler consumer (`SubconsciousDebateSchedulerConsumer`).

Key runtime behavior:
- debounce + idempotent trigger queueing.
- persisted turns/outcomes/metrics.
- validation gates protected identity updates.
- high-confidence auto-approval for `requiresUserInput` outcomes (configurable threshold).
- manual approve/reject + decision note + optional rerun queue supported via API.

## 10. Scheduled actions system
Implemented:
- persistence entity/table for scheduled actions,
- store abstraction (`IScheduledActionStore`),
- worker polling due actions (`ScheduledActionWorker`),
- API endpoints + SSE stream,
- memory tool functions for scheduling/listing/canceling.

Supported scheduled action types:
- `append_episodic`
- `queue_subconscious_debate`
- `store_memory`
- `execute_procedural_trigger`
- `invoke_webhook`

## 11. API surface (current mapped endpoints)
From `Program.cs` endpoint mapping and endpoint files:

Chat:
- `POST /api/chat`
- `POST /api/chat/stream`

Episodic:
- `POST /api/episodic/events`
- `GET /api/episodic/events/{sessionId}`

Semantic:
- `POST /api/semantic/claims`
- `GET /api/semantic/claims`
- `POST /api/semantic/claims/{claimId:guid}/evidence`
- `POST /api/semantic/contradictions`
- `POST /api/semantic/claims/{claimId:guid}/supersede`
- `POST /api/semantic/decay/run-once`

Procedural:
- `POST /api/procedural/routines`
- `GET /api/procedural/routines`

Self model:
- `GET /api/self-model/preferences`
- `POST /api/self-model/preferences`

Consolidation:
- `POST /api/consolidation/run-once`

Tool audits:
- `GET /api/tool-invocations`

Eventing:
- `GET /api/eventing/events`
- `GET /api/eventing/events/stream` (SSE)

Reasoning / planning / identity / truth:
- `POST /api/reasoning/run-once`
- `POST /api/planning/plan`
- `POST /api/planning/apply`
- `POST /api/identity/run-once`
- `POST /api/truth/run-once`

Subconscious:
- `GET /api/subconscious/debates/{sessionId}`
- `GET /api/subconscious/debates/detail/{debateId:guid}`
- `GET /api/subconscious/debates/{debateId:guid}/turns`
- `GET /api/subconscious/debates/{debateId:guid}/outcome`
- `GET /api/subconscious/debates/{debateId:guid}/review`
- `GET /api/subconscious/debates/{debateId:guid}/events`
- `POST /api/subconscious/debates/{debateId:guid}/approve`
- `POST /api/subconscious/debates/{debateId:guid}/decision`
- `POST /api/subconscious/debates/{debateId:guid}/reject`
- `POST /api/subconscious/debates/run-once`
- `GET /api/subconscious/debates/stream` (SSE)

Scheduled actions:
- `POST /api/scheduled-actions`
- `GET /api/scheduled-actions`
- `GET /api/scheduled-actions/stream` (SSE)
- `POST /api/scheduled-actions/{actionId:guid}/cancel`

## 12. Frontend status (Angular app)
Current UI (`frontend/cognitive-memory-chat/src/app/app.ts` + `app.html`) includes:
- multi-page console: `overview`, `chat`, `debates`, `analytics`, `operations`,
- chat streaming,
- automatic selection of latest assistant reply on send,
- right-side live event feed filtered by selected assistant reply window,
- live SSE streams for:
  - outbox events,
  - subconscious debate lifecycle,
  - scheduled actions.
- debates page with:
  - debate list/detail,
  - turns,
  - outcome/review preview,
  - approve/reject/rerun actions,
  - user decision input and queued rerun support.

## 13. End-to-end flows
### 13.1 Chat flow
1. User sends message to `/api/chat/stream`.
2. API calls `ChatService`.
3. `ChatService` loads working memory context from Redis.
4. Prompt is built with system policy + runtime context.
5. SK gateway streams from model; model may auto-call memory tools.
6. Final assistant response is appended to working memory.
7. User message is appended to episodic memory.
8. UI receives streamed text and then pulls tool evidence.

### 13.2 Memory write -> event flow
1. Repository writes domain state.
2. Repository writes outbox message in same unit of work.
3. `OutboxDispatcherWorker` polls pending outbox rows.
4. Publisher dispatches (in-process or RabbitMQ).
5. Consumers run (idempotent checkpoints written).
6. Outbox row transitions to published/dead-letter on retry policy.

### 13.3 Subconscious flow
1. Domain event arrives (e.g., episodic created).
2. `SubconsciousDebateSchedulerConsumer` queues debate topic per session.
3. `SubconsciousDebateWorker` picks queued session.
4. Debate runs through group chat orchestration, turn-by-turn persisted.
5. Outcome validated; if allowed, applied; else await user or fail.
6. Lifecycle events emitted and visible via SSE/UI.

### 13.4 Scheduled action flow
1. Action scheduled via API/tool.
2. Row stored with `runAtUtc`.
3. Worker claims due actions and executes typed action handler.
4. Success/failure/retry/cancel state persisted and evented.
5. UI displays live updates via `/api/scheduled-actions/stream`.

## 14. What it currently does not do (or is not complete)
- No explicit authentication/authorization/RBAC is wired in API pipeline.
- No strict multi-tenant isolation model beyond sessionId conventions.
- No guaranteed exactly-once side effects across external boundaries (idempotency is local checkpoint based; external webhook side-effects still need endpoint-side idempotency).
- OpenAI-compatible `/v1/*` facade is not currently mapped in API endpoints, despite gateway routing for `/v1/**` existing.
- Frontend production build currently fails CSS budget threshold (see status below).
- Some docs in repo are stale vs current endpoint/config behavior.

## 15. Current runtime/config status
From `src/CognitiveMemory.Api/appsettings.Development.json`:
- Eventing enabled with `RabbitMq` transport.
- RabbitMQ enabled at `localhost:5672` with guest/guest.
- Subconscious debate enabled.
- Scheduled actions enabled.
- Consolidation worker enabled, interval `1800s` (30 min) in dev.
- Decay/Reasoning/Identity/Truth workers enabled with slower intervals.

From `src/CognitiveMemory.Api/appsettings.json`:
- Eventing defaults to `InProcess` transport.
- Consolidation interval `3600s` (60 min) in base config.

## 16. Verified build/test status (this run)
Executed on 2026-02-22 in this workspace:
- `dotnet build CognitiveMemory.slnx -v minimal` -> **PASS**
- `dotnet test CognitiveMemory.slnx --no-build -v minimal` -> **PASS**
  - `CognitiveMemory.Application.Tests`: 5 passed
  - `CognitiveMemory.Api.Tests`: 26 passed
- `npm run -s build` (frontend) -> **FAIL**
  - error: CSS budget exceeded in `src/app/app.css`.
  - also one Angular template optional-chain warning.

## 17. Operational risks and watchpoints
- RabbitMQ connectivity/auth mismatch will still impact event transport in dev if AppHost env and API runtime config diverge.
- Multiple long-running hosted services increase contention risk if DB/Redis resources are constrained.
- `PayloadJson.ToLower().Contains(sessionId)` filtering in event queries is functional but expensive at scale; consider indexed/session-tagged columns for high throughput.
- Debate and scheduled-action systems are now deeply integrated; regression coverage should keep expanding around approval/apply paths.

## 18. Suggested immediate next hardening items
1. Fix frontend CSS budget config or split styles so CI/frontend build passes reliably.
2. Add authn/authz for write endpoints and debate approvals.
3. Add integration tests for scheduled actions + debate approval/apply side effects.
4. Add explicit environment sanity checks for RabbitMQ connection settings at startup.
5. Reconcile README/docs with current endpoint reality (including `/v1` routing vs actual handlers).
