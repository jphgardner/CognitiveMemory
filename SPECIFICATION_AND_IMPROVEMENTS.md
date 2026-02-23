# CognitiveMemory: Current System Specification and Improvement Areas

Last updated: 2026-02-23
Basis: direct inspection of runtime wiring, API endpoints, services, persistence model, and tests in this repository.

## 1. Product Scope (What Exists Today)

CognitiveMemory is a companion-centric memory platform with:
- Authenticated chat with LLM/tool orchestration.
- Multi-layer memory (working, episodic, semantic, procedural, self-model).
- Event-driven outbox processing (in-process or RabbitMQ transport).
- Subconscious debate orchestration with approval/review flows.
- Scheduled actions with worker execution and SSE visibility.
- Companion lifecycle + per-companion cognitive profile/versioning.
- Angular operations console and workspace views.

## 2. Runtime and Deployment Topology

Primary local entrypoint:
- `dotnet run --project CognitiveMemory.AppHost/CognitiveMemory.AppHost.csproj`

AppHost provisions:
- PostgreSQL with pgvector (`pgvector/pgvector:pg17`)
- Redis
- RabbitMQ (management image)
- API project
- Angular frontend
- YARP gateway on host port `8080`

Gateway routing:
- `/api/**` -> API
- `/v1/**` -> API
- `/**` -> frontend

## 3. Architectural Layers

- Domain (`src/CognitiveMemory.Domain`): memory entities/enums/value records.
- Application (`src/CognitiveMemory.Application`): orchestration services and ports.
- Infrastructure (`src/CognitiveMemory.Infrastructure`): EF Core persistence, repositories, SK adapters, eventing, workers.
- API (`src/CognitiveMemory.Api`): minimal APIs, middleware, auth, hosted workers.
- Frontend (`frontend/cognitive-memory-chat`): Angular console UI.

Dependency direction is Clean-Architecture style (API/Infrastructure depend on Application + Domain).

## 4. Security and Tenancy Model

Authentication/authorization:
- JWT auth is enabled globally for non-auth routes.
- Most route groups use `RequireAuthorization()`.
- Auth endpoints exist for register/login/me.

Companion scoping:
- Endpoints typically resolve ownership via `CompanionOwnershipService` before access.
- Companion `UserId` ties data access to authenticated users.

DB row-level security:
- Middleware sets `app.current_user_id` and `app.bypass_rls`.
- Migration enables RLS for `Companions` and several companion-scoped tables.

## 5. Core Data Model (Persisted)

Main EF Core sets in `MemoryDbContext` include:
- Episodic events
- Semantic claims + embeddings + evidence + contradictions
- Procedural routines + routine metrics
- Self preferences
- Tool invocation audits
- Outbox messages + consumer checkpoints
- Scheduled actions
- Subconscious debate sessions/turns/outcomes/metrics
- Memory relationships
- Companion and cognitive profile state/audits/runtime traces
- Portal users (auth)

## 6. Implemented Processing Pipelines

### 6.1 Chat Pipeline
- `/api/chat` and `/api/chat/stream` call `ChatService`.
- Working-memory context is injected.
- Semantic Kernel gateway can call memory tools.
- User interaction is appended into episodic memory.

### 6.2 Event-Driven Pipeline
- Writes enqueue outbox records via `IOutboxWriter`.
- `OutboxDispatcherWorker` publishes pending/failed events.
- Consumer dispatcher runs registered consumers with checkpoint idempotency.
- Dead letters can be replayed by recovery worker.

### 6.3 Cognitive Workers
- Consolidation
- Decay
- Reasoning
- Identity evolution
- Truth maintenance

### 6.4 Subconscious Debate Pipeline
- Debate sessions are queued and processed.
- Turns/outcomes/metrics are persisted.
- Review, approval, rejection, decision endpoints exist.
- SSE streams provide runtime updates.

### 6.5 Scheduled Actions Pipeline
- API + tools can create actions.
- Worker claims due actions and executes typed handlers.
- Status lifecycle is queryable and streamable.

## 7. API Surface (Current Families)

- Auth: register/login/me
- Companions: list/create/archive
- Companion cognitive profile: get, validate, versioning, stage/activate/rollback, audit, traces, simulate
- Chat: request + stream
- Episodic memory
- Semantic memory + evidence/contradictions/supersession/decay
- Procedural memory
- Self model
- Consolidation/reasoning/planning/identity/truth run-once endpoints
- Eventing queries + stream
- Tool invocation audits
- Scheduled actions + stream + cancel
- Subconscious debate queries/actions + stream
- Memory relationship APIs
- Workspace summary/packet/metrics/timeline/detail APIs

## 8. Frontend Status

Angular console includes:
- Auth pages
- Portal/workspace views
- Console pages: overview, chat, memory, debates, analytics, operations
- Chat streaming + SSE feeds for eventing/debates/scheduled actions
- Companion-aware workflow via selected companion context

## 9. Verification Snapshot

Latest run in this workspace (2026-02-23):
- `dotnet test CognitiveMemory.slnx -v minimal`
- Result: PASS
- Test totals: 38 passed (5 application, 33 API), 0 failed

## 10. Prioritized Improvement Areas

### P0 (Highest impact / correctness and reliability)

1. Unhandled worker exceptions can terminate critical loops.
- `src/CognitiveMemory.Api/Background/ReasoningWorker.cs`
- `src/CognitiveMemory.Api/Background/IdentityEvolutionWorker.cs`
- `src/CognitiveMemory.Api/Background/TruthMaintenanceWorker.cs`
- `src/CognitiveMemory.Api/Background/DecayWorker.cs`
- These workers execute service calls without per-cycle exception guards; a single runtime fault can stop processing for that worker.

2. DB query patterns are non-sargable in hot paths (`ToLower().Contains(...)`), causing full scans and scale risk.
- `src/CognitiveMemory.Api/Endpoints/EventingEndpoints.cs`
- `src/CognitiveMemory.Api/Endpoints/WorkspaceEndpoints.cs`
- `src/CognitiveMemory.Api/Endpoints/ToolInvocationAuditEndpoints.cs`
- `src/CognitiveMemory.Infrastructure/Repositories/SemanticMemoryRepository.cs`
- Use normalized columns / full-text search / trigram index patterns instead of runtime `LOWER(column) LIKE '%...%'`.

3. Workspace endpoint ownership parsing is inconsistent with other ownership checks.
- `src/CognitiveMemory.Api/Endpoints/WorkspaceEndpoints.cs`
- It requires the auth subject to parse as Guid; other endpoints treat user id as string claim. This can break workspace APIs under non-Guid identity providers.

### P1 (Security and platform hardening)

4. RLS coverage should be expanded to all companion-scoped tables (defense in depth).
- `src/CognitiveMemory.Infrastructure/Persistence/Migrations/20260222184433_EnableCompanionRlsPolicies.cs`
- Current policy list omits some companion-bearing tables such as subconscious turns/outcomes/metrics and cognitive profile tables.

5. Documentation is materially out of date versus actual API.
- `README.md`
- Endpoint list and `/v1` facade claims do not match current mapped endpoints in API startup.

6. `/v1/**` is routed by gateway but no OpenAI-compatible endpoint set is mapped in API.
- `CognitiveMemory.AppHost/Program.cs`
- `src/CognitiveMemory.Api/Program.cs`
- Either implement compatible handlers or remove public claims/routing to avoid integration confusion.

### P2 (Engineering quality and maintainability)

7. Large generated build artifacts are tracked in git history.
- `src/*/artifacts/out/**`
- This increases repository size/noise and slows review cycles.

8. Endpoint ownership checks are duplicated in places rather than uniformly using `CompanionOwnershipService`.
- Example: `src/CognitiveMemory.Api/Endpoints/WorkspaceEndpoints.cs`
- Consolidating ownership resolution reduces drift and logic divergence.

9. Test coverage should expand for worker failure/retry behavior and event/scheduler/subconscious integration.
- Current tests validate many API flows but do not yet provide broad resilience coverage across background worker failure paths.

## 11. Suggested Improvement Sequence

1. Reliability hardening for background workers (P0-1).
2. Query/index strategy for eventing/workspace/semantic search hot paths (P0-2).
3. Ownership consistency + RLS policy expansion (P0-3, P1-4).
4. Docs/API contract reconciliation, including `/v1` posture (P1-5, P1-6).
5. Repo hygiene and test expansion (P2-7 to P2-9).

