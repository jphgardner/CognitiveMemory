# Mission-First Cognitive System Plan (V2)
Date: 2026-02-13
Status: Active plan candidate

## Mission (North Star)
Build a persistent, evidence-grounded cognitive system that remembers, reasons, and revises its beliefs over time.

This system must move beyond stateless pattern prediction toward structured, self-correcting intelligence where:
- every claim is traceable,
- every contradiction is surfaced,
- uncertainty is explicit.

Trust is earned through consistency, transparency, and epistemic humility.

## Product Thesis
A single user-facing chat agent can be autonomous and still trustworthy if we combine:
- strict memory contracts,
- deterministic guardrails,
- auditable decision traces,
- background LLM conscience processes that continuously improve memory quality.

## Core Outcome We Are Optimizing For
Not “best sounding answer,” but “most defensible answer under evidence and uncertainty constraints.”

## Non-Negotiable System Invariants
1. No claim without evidence.
2. No destructive overwrite of belief history.
3. Contradictions are first-class records, never hidden.
4. Every factual answer has traceable evidence lineage.
5. Uncertainty must be surfaced when evidence is weak or conflicting.
6. All high-impact decisions are replayable from stored trace data.

## Target Operating Model

### 1. One User-Facing Brain
- A single `ChatAgent` handles conversation.
- The user never has to manage internal roles.

### 2. Dual LLM Planes
- Inline plane (request path): fast reasoning, tool use, response generation.
- Side-effect plane (background): deeper analysis and memory quality refinement.

### 3. Internal Conscience Swarm
Use specialist internal roles for quality governance:
- `Verifier` (citation/evidence sufficiency)
- `ContradictionAnalyst` (conflict detection + severity)
- `Calibrator` (confidence/uncertainty tuning)
- `PolicyJudge` (Approve/Revise/Downgrade/Block)

The swarm is internal and auditable; the user-facing interface stays single-agent.

## Architecture Decisions

### Decision A: Database is Source of Truth
- LLM output is advisory unless persisted through validated write paths.
- Belief transitions are append/transition, not overwrite.

### Decision B: Plugin-Mediated Autonomy
- Agent accesses memory only through typed plugins/tools.
- Tools enforce policy, idempotency, and audit envelopes.

### Decision C: Evented Side Effects
- Write tools emit events/outbox records.
- Background workers run expensive LLM analysis asynchronously.

### Decision D: Deterministic Safety Envelope
- Tool budgets, timeouts, permission levels, and response schemas are platform-controlled.
- LLM cannot bypass operational safety constraints.

## Data and Belief Lifecycle
1. Input arrives (chat/tool/document).
2. Minimal authoritative write occurs (if needed).
3. Outbox event emitted.
4. Side-effect workers enrich, reconcile, calibrate.
5. Contradiction and policy checks run.
6. Belief state updated via lifecycle transitions.
7. Future retrieval reflects improved state with explicit provenance.

## Tooling Contract Standard (All Plugins)
Each tool returns a strict envelope:
- `ok`
- `code`
- `message`
- `data`
- `idempotencyKey`
- `eventIds`
- `traceId`

This is mandatory for reliable agent behavior and deterministic post-processing.

## Permission Model
Define runtime policy modes:
- `ReadOnly`
- `ReadWriteSafe`
- `Privileged`

Default user chat mode is `ReadOnly` unless explicit policy elevation applies.

## Uncertainty and Confidence Policy
Every final answer should include:
- confidence score,
- uncertainty flags,
- contradiction indicators,
- citation references.

Low confidence or unresolved severe contradictions require softened language or output downgrade.

## Observability and Audit Model
Capture for each request:
- request id,
- model id,
- tool call sequence,
- tool inputs/outputs hash,
- conscience decision,
- policy version,
- emitted event ids.

Capture for each side-effect job:
- source event id,
- retries,
- completion status,
- produced state transitions.

## Execution Roadmap (Mission-Aligned)

### Phase 0: Integrity Baseline (Now)
Objective:
- lock in invariants and guardrails before adding autonomy depth.

Deliverables:
- strict tool envelope adopted across plugins,
- idempotency enforcement for mutating tools,
- queue/outbox reliability rules,
- logging profile focused on signal over noise.

Exit criteria:
- all writes are idempotent,
- all tool responses are schema-valid,
- no silent failure path for ingest/analysis jobs.

### Phase 1: Single-Agent Reliability
Objective:
- make `ChatAgent` stable and trustworthy on inline path.

Deliverables:
- one-agent orchestration with bounded budgets,
- read tools productionized,
- write tools policy-gated,
- mandatory fallback behaviors for empty/invalid LLM outputs.

Exit criteria:
- 99% successful chat responses with non-empty assistant output,
- deterministic fallback coverage for all critical failure points.

### Phase 2: Side-Effect Intelligence
Objective:
- deepen quality without increasing user latency.

Deliverables:
- outbox/event contracts finalized,
- background LLM workers for contradiction analysis and calibration,
- retrieval enrichment jobs.

Exit criteria:
- side-effect jobs are retry-safe and replayable,
- contradiction detection and calibration updates visible in subsequent reads.

### Phase 3: Conscience Decision Engine
Objective:
- formalize epistemic governance.

Deliverables:
- `PolicyJudge` decision model,
- reason code taxonomy,
- decision persistence with policy version pinning.

Exit criteria:
- each answer can be audited through conscience decision trace,
- revise/block decisions are reproducible.

### Phase 4: Adaptive Belief Revision Loop
Objective:
- self-correction over time.

Deliverables:
- retrospective quality evaluation,
- calibration tuning from observed correctness,
- contradiction cluster learning workflows.

Exit criteria:
- measurable improvement in calibration and contradiction surfacing over rolling windows.

### Phase 5: Operational Trust Surfaces
Objective:
- make system cognition inspectable in real time.

Deliverables:
- outbox queue health summaries and recent-event inspection APIs,
- calibration trend summaries and reason-code distribution APIs,
- explicit published policy catalog endpoint (version, decisions, reason taxonomy).

Exit criteria:
- operators can diagnose stuck side-effects and policy drift from API surfaces alone,
- policy and reason-code semantics are externally inspectable.

### Phase 6: Governed Confidence Adaptation
Objective:
- convert calibration recommendations into bounded belief updates safely.

Deliverables:
- policy-gated confidence write-back rules (decision allow-list, risk threshold),
- bounded per-update confidence delta controls,
- explicit outbox event for applied confidence changes.

Exit criteria:
- confidence write-back is deterministic, bounded, auditable, and disableable by config.

## Execution Status Update (2026-02-13)
1. Phase 0: Completed.
2. Phase 1: Completed implementation slice (answer timeout/fallback reliability path, non-empty response enforcement, policy-gated write tooling).
3. Phase 2: Completed implementation slice (durable outbox + LLM conscience worker + enrichment/calibration side-effects).
4. Phase 3: Completed implementation slice (persisted policy decisions with reason-code taxonomy and replay/read API surface).
5. Phase 4: Completed initial implementation slice (calibration history persistence and calibrated-confidence retrieval scoring loop).
6. Phase 5: Completed implementation slice (ops observability endpoints + published policy catalog endpoint).
7. Phase 6: Completed implementation slice (policy-gated, bounded confidence write-back with explicit outbox trace event).

## KPI Framework (Mission Metrics)
1. Citation coverage rate.
2. Contradiction surfacing rate.
3. Confidence calibration error.
4. Percent of responses with explicit uncertainty when warranted.
5. Replay success rate for revised/blocked decisions.
6. P95 and P99 chat latency.
7. Side-effect job completion SLA and retry success.

## Risk Register and Mitigations

Risk: Agent over-writes memory aggressively.
Mitigation: write mode defaults to `ReadOnly`; explicit policy elevation required.

Risk: Background jobs produce drift or conflicting updates.
Mitigation: lifecycle transitions + policy-gated merge rules + contradiction records.

Risk: LLM output schema instability.
Mitigation: strict response envelopes and deterministic reducers.

Risk: Latency regressions from excessive inline tooling.
Mitigation: hard tool budgets; push heavy work to side-effect plane.

Risk: False confidence under sparse evidence.
Mitigation: mandatory uncertainty policy and confidence calibration checks.

## What “Good” Looks Like in Production
- The system can explain what it believes and why.
- It can show when beliefs conflict.
- It can admit uncertainty explicitly.
- It can revise beliefs with traceable causality.
- Users can audit important answers and trust the process, not just the prose.

## Immediate Next Implementation Slice (Status)
1. Done: Normalize all plugin responses to the standard envelope.
2. Done: Add idempotency key enforcement for all mutating tools.
3. Done: Introduce durable outbox records for write-triggered side effects.
4. Done (Scaffold): Implement first conscience worker (`ContradictionAnalyst + Calibrator`) over outbox events.
5. Done (Scaffold): Add policy-judge decision persistence scaffold.
6. Done: Add operational trust surfaces (`/api/v1/ops/outbox/*`, `/api/v1/ops/calibration/*`).
7. Done: Publish policy catalog endpoint (`/api/v1/conscience/policy`).
8. Done: Add governed confidence write-back with bounded policy controls.
