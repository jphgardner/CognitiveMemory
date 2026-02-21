# Cognitive Memory System Sprint Plan
Generated: 2026-02-14

## Sprint 0 - Foundation and Guardrails
Duration: 3 days

Goal:
- Lock clean architecture boundaries and delivery workflow.

Scope:
- Confirm layer direction: `Api -> Application -> Domain`, `Infrastructure` implements application ports only.
- Establish coding standards, naming, and folder conventions.
- Add CI checks (`build`, lint/style, test).

Deliverables:
- Architecture decision record for dependency direction.
- Baseline CI pipeline.
- Initial observability/logging standard for requests and use-cases.

Definition of Done:
- Build passes in CI.
- No project reference violates clean architecture.
- Team can run app locally with one command.

## Sprint 1 - Working Memory + Chat Orchestration
Duration: 1 week

Goal:
- Deliver stable working-memory chat loop through Semantic Kernel.

Scope:
- Finalize chat use-case in Application.
- Add conversation/session context model for short-term memory.
- Add input validation and error contracts.
- Keep LLM integration in Infrastructure via SK adapter.

Deliverables:
- `POST /api/chat` production-ready contract.
- Working memory context lifecycle (session-scoped).
- Configurable provider support (`Ollama`, `OpenAI`).

Definition of Done:
- Integration tests for chat endpoint and cancellation/timeout handling.
- No Semantic Kernel reference in Domain/Application.
- P95 response latency baseline recorded.

## Sprint 2 - Episodic Memory (Event Timeline)
Duration: 1 week

Goal:
- Persist traceable events with time and source.

Scope:
- Add episodic event aggregate/entity and repository port.
- Implement append-only event write path.
- Add read endpoints for time-range queries.
- Include source references and actor metadata.

Deliverables:
- Episodic write/read use-cases.
- Database schema + migrations for episodic timeline.
- API endpoints for timeline retrieval.

Definition of Done:
- Event append is idempotent where required.
- Query by time range works with pagination.
- Tests cover persistence and API contracts.

## Sprint 3 - Semantic Memory (Claims + Evidence)
Duration: 1.5 weeks

Goal:
- Build belief store with confidence and evidence links.

Scope:
- Claim model: subject/predicate/object-value, confidence, scope, validity.
- Evidence model + association to claims.
- Claim query and mutation use-cases.
- Explicit contradiction records (no silent overwrite).

Deliverables:
- Claim management API and application services.
- Persistence for claims/evidence/contradictions.
- Basic confidence update rules.

Definition of Done:
- Contradictions are stored and queryable.
- Claims are version-traceable.
- Tests validate invariants and conflict behavior.

## Sprint 3.5 - Agentic Memory Tooling (Semantic Kernel Plugins)
Duration: 4 days

Goal:
- Let the LLM call memory tools directly via Semantic Kernel plugins under policy control.

Scope:
- Create SK plugin surface for episodic and semantic memory operations.
- Add tool invocation policy (enabled tools, max tool calls, timeout).
- Keep plugin implementation in Infrastructure only.
- Capture tool invocation telemetry and failures.

Deliverables:
- `MemoryTools` plugin (claims, evidence, contradictions, episodic query/write).
- Config flags to enable/disable auto-tool invocation per environment.
- Safety checks for write operations.

Definition of Done:
- Chat flow can auto-invoke plugin tools when model supports function calling.
- Tool calls are auditable in logs.
- Application and Domain stay SK-free.

## Sprint 4 - Consolidation Pipeline (Episodic -> Semantic)
Duration: 1 week

Goal:
- Promote episodic patterns into semantic claims.

Scope:
- Background consolidation worker.
- Promotion heuristics: repetition, user-confirmation weighting.
- Low-value filtering and promotion thresholds.

Deliverables:
- Scheduled consolidation job.
- Promotion decision logs and telemetry.
- Configurable thresholds in settings.

Definition of Done:
- Consolidation runs safely and is restart-tolerant.
- Promotions include evidence links back to episodes.
- Benchmark report for consolidation throughput.

## Sprint 5 - Procedural Memory (Routines/Playbooks)
Duration: 1 week

Goal:
- Represent and apply reusable procedures.

Scope:
- Procedure model: trigger, ordered steps, checkpoints, outcomes.
- Retrieval by trigger/context.
- Hook procedural recall into chat orchestration.

Deliverables:
- Procedure CRUD + retrieval APIs.
- Execution guidance output in chat responses.
- Safety checks for stale procedures.

Definition of Done:
- Procedure retrieval quality validated with test scenarios.
- Applied procedure references appear in response metadata.
- No direct infra leakage into Application.

## Sprint 6 - Self Model + Preference Memory
Duration: 1 week

Goal:
- Persist stable identity/preferences safely.

Scope:
- Preference and long-term goal model.
- Explicit consent and sensitivity handling rules.
- Self-model retrieval for response shaping.

Deliverables:
- Self-model storage and update flows.
- Preference-aware response formatting behavior.
- Policy checks for restricted data categories.

Definition of Done:
- Preference updates are auditable.
- Sensitive-field write protections enforced.
- End-to-end tests for preference-conditioned replies.

## Sprint 7 - Forgetting, Decay, and Truth Maintenance
Duration: 1.5 weeks

Goal:
- Prevent stale memory accumulation and support revision.

Scope:
- TTL/decay policies by memory type.
- Reinforcement signals reset/slow decay.
- Supersession chains and conflict resolution strategy.
- Confidence recalibration loop.

Deliverables:
- Decay scheduler and archival policy.
- Revision API for superseding claims.
- Conflict resolution reports.

Definition of Done:
- Expired/transient memory prunes predictably.
- Superseded beliefs remain historically traceable.
- Contradiction and revision audits pass.

## Sprint 8 - Hardening and Production Readiness
Duration: 1 week

Goal:
- Prepare for stable release.

Scope:
- Security review, rate limiting, auth integration.
- Full observability dashboards.
- Performance tuning for retrieval and LLM calls.
- DR/backups and migration strategy.

Deliverables:
- Release candidate build.
- Runbooks (incident, recovery, rollback).
- SLOs + alert thresholds.

Definition of Done:
- Load, resilience, and recovery tests pass.
- Release checklist signed off.
- Documentation updated for operations and development.

## Cross-Sprint Rules
- Keep `Application` as the orchestration core; external SDKs stay in `Infrastructure`.
- Every sprint includes:
  - API contract tests
  - application unit tests
  - persistence/integration tests (where relevant)
- Every persisted decision must remain explainable via source/evidence.

## Milestone Map
1. M1 (after Sprint 2): Chat + Working + Episodic baseline.
2. M2 (after Sprint 4): Semantic memory + consolidation loop live.
3. M3 (after Sprint 7): Full memory lifecycle with revision/decay.
4. M4 (after Sprint 8): Production-ready release.
