# CognitiveMemory Runtime Architecture (Mission-First Implementation)
Date: 2026-02-14
Status: Implemented baseline + Phase 1/2/3/4/5/6 execution slice + LLM entity-binding ingest

## What Is Implemented
This implementation aligns the runtime with the mission plan by adding:

1. A strict plugin tool response envelope across all Semantic Kernel plugins.
2. Idempotency enforcement for all mutating tools.
3. Durable outbox persistence for write-triggered side effects.
4. An LLM-backed conscience worker (`ContradictionAnalyst + Calibrator`) with deterministic fallback.
5. Policy decision persistence and replay/read endpoints.
6. Retrieval enrichment artifacts (`ClaimInsights`) and adaptive calibration history (`ClaimCalibrations`).
7. Operational trust surfaces for outbox and calibration telemetry.
8. Governed confidence write-back with bounded policy controls.

## Core Request Paths

### 1) User Chat Path (`/v1/chat/completions`)
1. OpenAI-compatible endpoint receives request.
2. Semantic Kernel chat agent (Ollama-backed) executes with tool access (`memory_recall`, `memory_write`, `memory_governance`, `grounding`).
3. Response is returned as OpenAI-compatible output (streaming SSE for `stream=true`).
4. If no successful `memory_write.create_claim` occurred, deterministic fallback attempts a tool-based `create_claim` for durable name facts.
5. Chat persistence follows configured mode:
   - `AgentOnly`: agent-owned writes only
   - `HybridFallback`/`SystemPostTurn`: optional system post-turn `memory_write.ingest_note` path
6. Tool responses are captured and returned in `metadata.toolExecutions` (including prefetch and post-turn writes).

### 1.1) Memory Recall Filtering
For narrower recall, the runtime supports:

- `memory_recall.search_claims` (broad)
- `memory_recall.search_claims_filtered` (filtered)

`search_claims_filtered` supports:
- `subjectFilter`
- `predicateFilter` (contains)
- `literalContains` (contains)
- `sourceTypeFilter` (matches evidence source type)
- `minConfidence` (0..1)
- `minScore` (0..1)
- `scopeContains` (contains)

### 2) Background Side-Effect Path (Outbox-Driven)
1. Mutating tools emit durable outbox events.
2. Outbox worker consumes `memory.document.ingested` and runs LLM extraction/entity binding/claim creation.
3. Newly emitted claim events are then processed for conscience/calibration/enrichment workflows.
4. All side effects run off outbox events, not from a chat-turn ingest queue.

### 2.1) LLM-Driven Entity Binding
- Claim extraction prompt now requests `subjectKey`, `subjectName`, and `subjectType` per claim.
- Chat requests now carry a stable OpenAI `user` key, propagated to ingest as `actorKey`.
- This makes first-person facts (for example, "my name is James") attach to a stable subject identity across turns instead of per-turn source refs.

### 3) Agent Mutating Tool Path
Mutating tools now follow this sequence:
1. Resolve/generate idempotency key.
2. Check `ToolExecutions` for cached response by (`toolName`, `idempotencyKey`).
3. If found, return cached envelope immediately.
4. Execute guarded write operation.
5. Emit durable outbox event(s).
6. Persist response in `ToolExecutions`.
7. Return strict envelope.

## Tool Response Contract (All Plugins)
All plugin functions return serialized JSON with this shape:

```json
{
  "ok": true,
  "code": "created",
  "message": "Claim created and outbox event emitted.",
  "data": { "claimId": "...", "status": "created" },
  "idempotencyKey": "memory_write.create_claim:...",
  "eventIds": ["..."],
  "traceId": "..."
}
```

Implemented across:
- `MemoryWritePlugin`
- `MemoryGovernancePlugin`
- `MemoryRecallPlugin`
- `GroundingPlugin`
- `ClaimExtractionPlugin`
- `DebateRolePlugin`

The debate orchestrator instructions explicitly tell model roles to read tool results from `data` and inspect `ok`.

## Persistence Model Additions

### New Tables
- `ToolExecutions`
  - Stores idempotent tool responses keyed by (`ToolName`, `IdempotencyKey`).
- `OutboxEvents`
  - Durable event queue for asynchronous side effects.
  - Supports statuses, attempts, lease locking, retry scheduling.
- `PolicyDecisions`
  - Stores persisted governance decisions with risk, policy version, reasons, and metadata.

Compatibility note:
- Migration `20260213234500_AddOutboxPolicyToolingScaffold` uses `IF NOT EXISTS` SQL so existing databases that already applied the initial migration can be upgraded safely.

### Existing Table Usage
- `Claims`, `Evidence`, `Contradictions`, `Documents` remain system-of-record memory tables.

## Outbox Event Types
Current emitted event types:
- `memory.document.ingested`
- `memory.claim.created`
- `memory.contradiction.flagged`
- `memory.claim.superseded`
- `memory.claim.retracted`
- `memory.claim.enriched`
- `memory.claim.calibration.recorded`
- `memory.claim.confidence.updated`
- `memory.answer.generated`
- `conscience.analysis.completed`

## Conscience Worker
Hosted service: `ConscienceOutboxWorker`

Behavior:
1. Polls `OutboxEvents` for reserveable events.
2. Processes `memory.document.ingested` by loading the stored document and running extraction/materialization pipeline.
3. Processes cognitive candidate claim events (`memory.claim.created`, `memory.contradiction.flagged`, `memory.claim.superseded`, `memory.claim.retracted`).
4. Loads claim state and contradiction status.
5. Runs LLM conscience analysis via `SemanticKernelConscienceAnalysisEngine` (with deterministic fallback if unavailable/timeout).
6. Persists a policy decision row (`SourceType = ConscienceWorker`).
7. Persists retrieval insight and calibration records (`ClaimInsights`, `ClaimCalibrations`).
8. Applies optional policy-gated confidence write-back to `Claims.Confidence` with bounded deltas.
9. Emits enrichment/calibration/confidence/completion events.
10. Marks source outbox event `Succeeded` (or `Failed` with retry delay).

This is the side-effect intelligence plane and remains auditable, with deterministic fallback behavior when model analysis is unavailable.

## LLM Conscience Analysis
`SemanticKernelConscienceAnalysisEngine` executes a dedicated prompt (`prompts/conscience/analyzer/prompt.md`) and returns:
- decision (`Approve|Downgrade|Revise|Block`)
- risk score
- recommended confidence
- reason codes
- concise summary + keywords for retrieval enrichment

If parsing/model execution fails, it falls back to a deterministic heuristic analyzer.

## Policy Decision Persistence

### Inline path decisions
`MemoryService.AnswerAsync(...)` now computes and persists policy decisions:
- Decision: `Approve`, `Downgrade`, `Revise`, or `Block`
- Risk score
- Policy version
- Reason codes

Decisioning factors include:
- citation presence,
- severe open contradictions,
- uncertainty flags.

### Conscience worker decisions
Conscience processing also writes decisions with event-linked metadata and reason codes.

### Decision replay/read APIs
- `GET /api/v1/conscience/decisions/recent`
- `GET /api/v1/conscience/decisions/{sourceType}/{sourceRef}`
- `GET /api/v1/conscience/replay/{requestId}`
- `GET /api/v1/conscience/policy`

## Operations APIs
- `GET /api/v1/ops/outbox/summary`
- `GET /api/v1/ops/outbox/events`
- `GET /api/v1/ops/calibration/summary`

## Configuration

### Added settings
In `appsettings.json` and `appsettings.Development.json`:

- `AgentTooling:Mode` (`ReadOnly|ReadWriteSafe|Privileged`)
- `ChatPersistence:Mode` (`AgentOnly|HybridFallback|SystemPostTurn`)
- `ConscienceOutboxWorker:PollIntervalSeconds`
- `ConscienceOutboxWorker:BatchSize`
- `ConscienceOutboxWorker:LeaseSeconds`
- `ConscienceOutboxWorker:RetryDelaySeconds`
- `SemanticKernel:ConscienceAnalysisTimeoutSeconds`
- `ConscienceCalibration:EnableClaimConfidenceWriteBack`
- `ConscienceCalibration:MaxRiskScoreForWriteBack`
- `ConscienceCalibration:MinDeltaToWriteBack`
- `ConscienceCalibration:MaxStepPerUpdate`
- `ConscienceCalibration:AllowedDecisions`

## Phase Coverage
1. Phase 0 Integrity Baseline: implemented.
2. Phase 1 Single-Agent Reliability: implemented in this slice through answer timeout/fallback, deterministic non-empty response behavior, and policy-gated write tooling.
3. Phase 2 Side-Effect Intelligence: implemented in this slice with durable outbox + LLM conscience analysis + enrichment/calibration emissions.
4. Phase 3 Conscience Decision Engine: implemented in this slice with persisted decision records, reason-code taxonomy, policy version pinning, and replay endpoints.
5. Phase 4 Adaptive Belief Revision Loop: implemented in this slice via persisted calibration history and confidence-aware retrieval scoring.
6. Phase 5 Operational Trust Surfaces: implemented in this slice with outbox/calibration observability endpoints and policy catalog API.
7. Phase 6 Governed Confidence Adaptation: implemented in this slice with policy-gated, bounded confidence write-back and explicit outbox trace events.

## Reliability Properties After This Implementation
- Mutating tool calls are idempotent by key.
- Mutating tool outputs are deterministic and schema-stable.
- Side effects are decoupled from request latency via durable outbox.
- Conscience actions are replayable through persisted event and decision history.
- Chat answers now have persisted policy judgments.
- Confidence adaptation is bounded and policy-governed, with audit events.

## Current Gaps (Intentional)
- Conscience analysis is single-pass prompt analysis, not yet role-separated internal swarm execution.
- No contradiction clustering workflow is implemented for multi-claim conflict themes.
- No longitudinal calibration quality report (predicted vs observed correctness) exists yet.

## Next Implementation Targets
1. Add role-separated conscience swarm execution (`Verifier`, `ContradictionAnalyst`, `Calibrator`, `PolicyJudge`) with explicit per-role prompt traces.
2. Add contradiction clustering and replay tooling.
3. Add longitudinal calibration quality scoring and trend alerting.
4. Add policy migration/version lineage support beyond a single active version constant.
