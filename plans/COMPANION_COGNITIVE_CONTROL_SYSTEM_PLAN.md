# Companion Cognitive Control System (CCCS) - Implementation Plan

## Purpose
Build a structured, versioned, runtime-enforced cognitive control layer so each companion can think, reason, remember, and respond differently in a predictable, safe, and auditable way.

This plan is intentionally aligned to the current codebase architecture:
- Companion model: `Companions` + ownership checks + JWT auth.
- Runtime path: `ChatService` -> `SemanticKernelChatGateway` -> `MemoryToolsPlugin`.
- Memory layers: working (Redis), episodic, semantic, procedural, self, relationships.
- Background cognition: consolidation, reasoning, truth maintenance, identity evolution, subconscious debate.
- Eventing: outbox + consumers + workers.
- Isolation baseline: companion-scoped columns + RLS middleware/policies.

---

## Current Baseline and Required Corrections

### What already exists and should be reused
- Strong companion/session concept: `Companions` table and `CompanionScopeResolver`.
- Companion-scoped storage in core memory tables (`CompanionId` on episodic/semantic/self/procedural/etc.).
- RLS middleware and companion policies for many core tables.
- Runtime retrieval and scoring logic already centralized in `MemoryToolsPlugin`.
- Debate, scheduling, and outbox pipelines already present and production-usable.

### Critical gaps to close before/with CCCS rollout
- Several cognitive services still run unscoped/global in read paths (`QueryRangeAsync`, unscoped `QueryClaimsAsync`, unscoped self/procedural lookups).
- Some reactive flows still create `"global"` session relationships.
- Some non-core projection tables are not companion-scoped and/or not RLS-protected.

### Non-negotiable constraint for CCCS
CCCS rollout includes companion-scoping hardening for all cognitive read/write paths and all user-visible projections, not only new profile tables.

---

## 1. Cognitive Profile System Design

Each companion gets one active **cognitive profile version** (immutable snapshot).
All runtime decisions must reference that version id/hash.

### 1.1 Profile dimensions (all configurable)

#### A. Attention and focus
- `focusStickiness` (0..1): persistence on current thread vs topic switching.
- `contextWindowAllocation`: token budgets by source (`working`, `episodic`, `semantic`, `procedural`, `self`).
- `explorationBreadth` (1..N): how many alternate lines of retrieval/reasoning are explored.
- `clarificationFrequency` (0..1): proactive clarification tendency.

#### B. Memory weighting and retrieval behavior
- `retrievalWeights`: weights for recency, semantic match, evidence strength, relationship-degree, confidence, and layer priors.
- `layerPriorityOverrides`: per-intent layer boosts.
- `maxCandidates`, `maxResults`, `dedupeSensitivity`.
- `writeThresholds`: minimum confidence/importance before persistence.
- `decayPolicy`: per-layer decay pace and reinforcement multiplier.

#### C. Reasoning style and structure
- `reasoningMode`: deductive, abductive, hybrid, heuristic-first.
- `structureTemplate`: terse, outline-first, evidence-first, action-first.
- `depth`: number of reasoning passes (bounded).
- `evidenceStrictness` (0..1): how strongly unsupported claims are filtered.

#### D. Verbosity, tone, and emotional expressivity
- `verbosityTarget`: concise, balanced, detailed.
- `toneStyle`: neutral/professional/coaching/etc.
- `emotionalExpressivity` (0..1): emotional mirroring strength.
- `formatRigidity` (0..1): strictness of output structure.

#### E. Reflection and debate frequency
- `selfCritiqueEnabled`.
- `selfCritiqueRate` (0..1): probability of an internal revision pass.
- `maxSelfCritiquePasses`.
- `subconsciousDebatePolicy`: trigger sensitivity, turn cap, termination thresholds.

#### F. Confidence and uncertainty handling
- `answerConfidenceThreshold`.
- `clarifyConfidenceThreshold`.
- `deferConfidenceThreshold`.
- `conflictEscalationThreshold`.
- `citationRequirementByDomain`.

#### G. Procedural vs adaptive behavior
- `procedurality` (0..1): preference for known routines/checklists.
- `adaptivity` (0..1): novelty allowed when routine fit is low.
- `policyStrictness` (0..1): tolerance for deviations.

#### H. Evolution over time
- `evolutionMode`: disabled, propose-only, supervised-auto.
- `maxDailyDelta` (0..1): bounded behavior drift.
- `learningSignals`: which metrics/events can influence future profile versions.
- `approvalPolicy`: human required vs auto for low-risk fields.

### 1.2 Runtime behavior contract
Profile values must affect:
- retrieval scoring,
- retrieval layer selection,
- prompt assembly,
- response shaping,
- internal critique/debate behavior,
- memory consolidation/decay/evolution thresholds.

No profile field may be prompt-only if it claims to control runtime behavior.

---

## 2. Configuration Architecture

### 2.1 Profile schema and storage model
Use immutable versioned artifacts with strict validation.

#### New tables

##### `CompanionCognitiveProfiles`
1. `CompanionId` (PK, FK -> `Companions.CompanionId`)
2. `ActiveProfileVersionId` (FK -> `CompanionCognitiveProfileVersions.ProfileVersionId`)
3. `StagedProfileVersionId` (nullable FK)
4. `CreatedAtUtc`, `UpdatedAtUtc`
5. `UpdatedByUserId`

##### `CompanionCognitiveProfileVersions`
1. `ProfileVersionId` (PK)
2. `CompanionId` (FK -> `Companions.CompanionId`)
3. `VersionNumber` (int, monotonic per companion)
4. `SchemaVersion` (e.g., `1.0.0`)
5. `ProfileJson` (jsonb/text)
6. `CompiledRuntimeJson` (jsonb/text)
7. `ProfileHash` (sha256 hex)
8. `ValidationStatus` (`Draft|Validated|Rejected|Active|Deprecated`)
9. `CreatedByUserId`
10. `ChangeSummary`
11. `ChangeReason`
12. `CreatedAtUtc`

Indexes:
- unique `(CompanionId, VersionNumber)`
- unique `(CompanionId, ProfileHash)`
- `(CompanionId, CreatedAtUtc desc)`

##### `CompanionCognitiveProfileAudits`
1. `AuditId` (PK)
2. `CompanionId`
3. `ActorUserId`
4. `Action` (`CreateVersion|Validate|Activate|Stage|Rollback|AutoRollback`)
5. `FromProfileVersionId` (nullable)
6. `ToProfileVersionId` (nullable)
7. `DiffJson`
8. `Reason`
9. `CreatedAtUtc`

##### `CompanionCognitiveRuntimeTraces`
1. `TraceId` (PK)
2. `CompanionId`
3. `SessionId`
4. `ProfileVersionId`
5. `RequestCorrelationId`
6. `Phase` (`retrieve|compose|generate|critique|finalize|persist`)
7. `DecisionJson`
8. `LatencyMs`
9. `CreatedAtUtc`

### 2.2 Existing-table extensions

#### `Companions`
- Keep existing columns.
- Add `ActiveCognitiveProfileVersionId` nullable during migration, then enforce non-null after bootstrap.

#### Companion-isolation hardening (required)
Add `CompanionId` and RLS where missing in user-visible cognitive projections:
- `SubconsciousDebateTurns`
- `SubconsciousDebateOutcomes`
- `SubconsciousDebateMetrics`
- `ProceduralRoutineMetrics`
- `ConflictEscalationAlerts`
- `UserProfileProjections`

### 2.3 Versioning and migration rules
- Profile schema uses SemVer.
- Backward-compatible additions: minor version.
- Breaking changes: major version + migration adapter.
- Runtime loads only validated + schema-compatible versions.

### 2.4 Validation rules
Validation occurs before version creation and before activation.

#### Structural validation
- JSON schema validation.
- Required fields for all dimensions.

#### Semantic validation
- Threshold ordering: `defer <= clarify <= answer`.
- All weights normalized where required.
- Budget constraints: token/tool/debate caps within platform max.
- Reflection limits consistent with latency budget.
- Evolution delta within allowed envelope.

#### Safety lint validation
Reject dangerous combinations, for example:
- very low defer threshold + high expressivity + low evidence strictness,
- high adaptivity + low policy strictness + high auto-evolution,
- unbounded critique/debate loops.

---

## 3. Runtime Integration

## 3.1 Core runtime components to add
- `ICompanionCognitiveProfileResolver`: resolve active compiled profile from `sessionId` or `companionId`.
- `ICognitivePolicyCompiler`: compile JSON profile to optimized runtime policy object.
- `ICognitiveResponseOrchestrator`: apply profile across retrieval, generation, critique, and finalization.
- `ICognitiveUncertaintyGate`: decide answer vs clarify vs defer.
- `ICognitiveConsolidationPolicy`: inject profile-driven thresholds into consolidation/reasoning/truth/evolution.

### 3.2 Memory retrieval weighting integration
Primary integration point: `MemoryToolsPlugin`.

Replace hard-coded scoring behaviors in:
- `ResolveRetrieveLayersAsync`
- `ComputeLayerPriorityBonus`
- `ComputeRecencyBonus`
- `ComputePayloadBonus`
- rank and candidate limits

with profile-driven policy fields.

Result:
- Companion A can be recency-heavy and concise.
- Companion B can be evidence-heavy and reflective.
- Same question, same memory, different controlled behavior.

### 3.3 Prompt construction integration
Primary integration point: `ChatService` prompt assembly.

Changes:
- Replace static system prompt composition with profile-aware prompt composer.
- Keep base platform policy fixed.
- Inject companion identity + cognitive directives from active profile.
- Preserve tool policy but profile can tune tool-use aggressiveness and clarification behavior.

### 3.4 Response generation integration
Apply profile to:
- verbosity target,
- structure template,
- emotional expressivity,
- procedural vs adaptive bias.

Implementation options:
1. Pre-generation controls in prompt and SK arguments.
2. Post-generation shaping pass for formatting/verbosity/tone bounds.
3. Confidence-based uncertainty gate before final output.

### 3.5 Debate and self-critique loops

#### Immediate response self-critique
- Optional hidden second pass (bounded by `maxSelfCritiquePasses` and latency budget).
- Triggered by profile `selfCritiqueRate` + low confidence margin.

#### Asynchronous subconscious debate
Primary integration points:
- `SubconsciousDebateService`
- `SubconsciousGroupChatManager`

Convert global `SubconsciousDebateOptions` usage to profile-aware resolved options per companion at runtime.

### 3.6 Consolidation and decay behavior
Primary integration points:
- `ConsolidationService`
- `CognitiveReasoningService`
- `TruthMaintenanceService`
- `IdentityEvolutionService`
- decay path in semantic service/worker

Required changes:
- Make run methods companion-scoped.
- Pass `CompanionId` through workers and reactive consumers.
- Apply profile-specific thresholds for extraction confidence, conflict penalties, uncertainty thresholds, and evolution aggressiveness.

---

## 4. Backend Design

### 4.1 Services

#### New services
- `CompanionCognitiveProfileService`
- `CompanionCognitiveProfileValidationService`
- `CompanionCognitiveProfileActivationService`
- `CompanionCognitiveProfileAuditService`
- `CompanionCognitiveRuntimeTraceService`
- `CompanionCognitiveEvolutionService` (propose next profile versions from metrics)

#### Existing services to refactor
- `ChatService`: make profile-aware for prompt + generation orchestration.
- `MemoryToolsPlugin`: profile-driven retrieval and write policies.
- `SubconsciousDebateService`: per-companion debate policy.
- `ConsolidationService`, `CognitiveReasoningService`, `TruthMaintenanceService`, `IdentityEvolutionService`: companion-scoped runs.

### 4.2 API surface (new)
All endpoints must require auth + companion ownership checks.

- `GET /api/companions/{companionId}/cognitive-profile`
- `GET /api/companions/{companionId}/cognitive-profile/versions`
- `POST /api/companions/{companionId}/cognitive-profile/validate`
- `POST /api/companions/{companionId}/cognitive-profile/versions`
- `POST /api/companions/{companionId}/cognitive-profile/stage`
- `POST /api/companions/{companionId}/cognitive-profile/activate`
- `POST /api/companions/{companionId}/cognitive-profile/rollback`
- `GET /api/companions/{companionId}/cognitive-profile/audit`
- `GET /api/companions/{companionId}/cognitive-profile/runtime-traces`
- `POST /api/companions/{companionId}/cognitive-profile/simulate` (preview behavior without activation)

### 4.3 Validation and safety mechanisms
- Schema validator + semantic validator + safety linter.
- Activation guard requiring validated status.
- Runtime hard clamps (platform max tokens/tool calls/debate turns/latency).
- Fallback to last-known-good profile on load/validation failure.
- Emit outbox events for profile lifecycle actions.

### 4.4 Isolation guarantees

#### Data-plane rules
- Every cognitive profile table row keyed by `CompanionId`.
- RLS policies mirroring existing companion policy pattern.
- Runtime resolution always `sessionId -> companionId -> profile version`.

#### Service-plane rules
- Remove/ban unscoped cognitive reads in repositories/services.
- Forbid `Guid.Empty` fallback in companion data paths.
- Forbid `"global"` synthetic session usage for companion memory edges.

#### Observability rules
- Runtime traces always include `CompanionId`, `SessionId`, `ProfileVersionId`.
- Add alert on any cross-companion read attempt.

### 4.5 Scalability model
- Cache active compiled profile by companion in memory + Redis.
- Invalidate cache on activation/rollback event.
- Keep profile lookups O(1) on hot path.
- Persist runtime traces asynchronously (batch write or outbox event ingestion).

---

## 5. Frontend Control Surface

Primary UI surfaces in current app:
- companion creation dialog,
- portal companion detail,
- workspace/console.

### 5.1 Companion creation flow
Extend create dialog to include:
- profile template selection,
- quick sliders for verbosity, reflection, decisiveness, emotional expressivity,
- optional advanced toggle.

Create companion API call should create companion + initial profile version + active binding atomically.

### 5.2 Edit flow (post-create)
Add a dedicated **Cognitive Profile** section (portal/workspace):
- current active profile summary,
- editable draft controls by category,
- validation status and warnings,
- simulate before activate.

### 5.3 Advanced tuning controls
- full JSON editor with schema-aware validation,
- per-dimension numeric controls,
- explicit memory weighting matrix,
- uncertainty threshold triad controls,
- debate/self-critique tuning panel,
- evolution policy panel.

### 5.4 Version history and rollback
- chronological version timeline,
- side-by-side diff viewer,
- activation history with actor/reason,
- one-click rollback to any validated previous version,
- display runtime metric delta between versions.

---

## 6. Safety and Stability

### 6.1 Guardrails against unstable behavior
- Field-level hard bounds with server-side clamps.
- Prevent activation of unsafe field combinations.
- Max reflection/debate loops and total token/tool budgets.
- Enforce latency ceilings and loop circuit breakers.

### 6.2 Hallucination amplification controls
- Profile-driven uncertainty gate before answer finalization.
- Require retrieval/evidence checks for memory claims.
- Penalize unsupported claims in ranking/finalization.
- Add contradiction sensitivity boost for high-risk domains.

### 6.3 Drift and over-adaptation controls
- Evolution writes only produce draft profile versions.
- Activation requires validation and policy approval gate.
- Daily behavior delta caps.
- Automatic rollback trigger if quality KPIs regress beyond threshold.

### 6.4 Cross-companion interference prevention
- Strict companion scoping at DB, repository, service, and API layers.
- No shared mutable profile state across companions.
- No global fallback session writes in relationship/debate pipelines.
- RLS policies extended to all cognitive-control tables.

---

## 7. Testing Strategy

### 7.1 Unit tests
- Profile schema validation and semantic rules.
- Safety linter constraints.
- Policy compiler determinism and hash stability.
- Retrieval scoring function behavior by profile weights.
- Uncertainty gate threshold behavior.

### 7.2 Integration tests (API + persistence)
- Create version -> validate -> activate -> rollback lifecycle.
- Ownership + RLS enforcement on cognitive profile endpoints.
- Runtime traces persisted with correct profile version linkage.

### 7.3 Behavior differentiation tests
- Same session memory + same prompt across two companions with different profiles.
- Assert measurable differences in:
  - verbosity,
  - clarification count,
  - uncertainty actions,
  - retrieval layer distribution,
  - self-critique/debate frequency.

### 7.4 Isolation tests
- Two users, multiple companions each.
- Verify no cross-companion retrieval in:
  - chat path,
  - memory tools,
  - background cognition workers,
  - debate pipelines,
  - projections/metrics endpoints.

### 7.5 Runtime influence tests
- Change only profile version; keep data/model constant.
- Verify runtime decisions (scoring, layer selection, critique loop) change as expected.
- Assert that profile hash in trace matches active version.

### 7.6 Rollback safety tests
- Activate new profile, run traffic, capture KPIs.
- Roll back to prior version, verify behavior envelope restoration.
- Verify no profile corruption and correct audit trail.

### 7.7 Load and reliability tests
- High companion cardinality with frequent profile reads.
- Cache hit/miss behavior for profile resolver.
- Worker throughput under profile-aware companion-scoped processing.

---

## Delivery Plan (Recommended Phases)

### Phase 1: Isolation hardening and profile persistence
- Add profile tables + migrations + RLS.
- Remove unscoped/global cognitive reads.
- Add companion-scoped signatures to cognitive services.

### Phase 2: Runtime wiring
- Profile resolver + compiler + validation service.
- Integrate into `ChatService`, `MemoryToolsPlugin`, and debate manager.
- Add runtime trace emission.

### Phase 3: API and UI
- Add profile CRUD/version/activation/rollback APIs.
- Add portal/workspace cognitive controls and version history.

### Phase 4: Evolution and auto-tuning
- Add profile proposal engine from metrics/events.
- Add approval workflow and guarded auto-adaptation.

### Phase 5: Hardening
- Differential behavior test suite.
- Isolation regression suite.
- Production SLO dashboards for profile-resolve latency, rollback success, and policy violations.

---

## Definition of Done
CCCS is considered complete when:
1. Every companion has an independently versioned active cognitive profile.
2. Profiles materially affect runtime retrieval/reasoning/response behavior.
3. Cross-companion memory leakage is blocked in both request and background paths.
4. Profile updates are fully auditable and safely rollbackable.
5. UI supports creation-time tuning, later edits, advanced controls, and rollback history.
6. Differential behavior and isolation tests are passing in CI.
