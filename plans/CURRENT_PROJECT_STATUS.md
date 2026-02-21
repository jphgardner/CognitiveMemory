# Current Project Status

Last updated: 2026-02-21
Source of truth: current repository code under `src/`, `frontend/`, `tests/`, and app host wiring.

## Executive Summary
- The project is in a major refactor state, with a new memory-centric architecture implemented across Domain, Application, Infrastructure, API, and Angular frontend.
- Core memory layers are present in code: working, episodic, semantic, procedural, and self-model.
- Background workflows for consolidation and decay are implemented and wired as hosted services.
- API endpoint surface is implemented for chat, memory operations, consolidation, decay, and tool invocation audit reads.
- CI exists, but local full solution test execution is currently blocked in this environment by missing `Aspire.AppHost.Sdk` restore resolution.

## Implemented Architecture (Code-Verified)
- `src/CognitiveMemory.Domain/Memory`: memory models and enums.
- `src/CognitiveMemory.Application`: orchestration services by concern:
  - `ChatService`
  - `EpisodicMemoryService`
  - `SemanticMemoryService`
  - `ProceduralMemoryService`
  - `SelfModelService`
  - `ConsolidationService`
- `src/CognitiveMemory.Infrastructure`:
  - EF Core persistence (`MemoryDbContext` + migrations)
  - Redis working memory store
  - Semantic Kernel gateways/plugins/tooling guard
  - Repositories for episodic/semantic/procedural/self-model/consolidation state/tool audits
- `src/CognitiveMemory.Api`:
  - Minimal API endpoint mapping
  - rate limiting
  - request metrics + request context logging middleware
  - hosted workers for consolidation and decay
- `CognitiveMemory.AppHost`:
  - Aspire composition for Postgres, Redis, API, Angular frontend, and YARP gateway.

## Active API Surface (Code-Verified)
From endpoint mappings under `src/CognitiveMemory.Api/Endpoints`:

- `POST /api/chat`
- `POST /api/chat/stream`
- `POST /api/episodic/events`
- `GET /api/episodic/events/{sessionId}`
- `POST /api/semantic/claims`
- `GET /api/semantic/claims`
- `POST /api/semantic/claims/{claimId:guid}/evidence`
- `POST /api/semantic/contradictions`
- `POST /api/semantic/claims/{claimId:guid}/supersede`
- `POST /api/semantic/decay/run-once`
- `POST /api/procedural/routines`
- `GET /api/procedural/routines`
- `GET /api/self-model/preferences`
- `POST /api/self-model/preferences`
- `POST /api/consolidation/run-once`
- `GET /api/tool-invocations`

## Data Layer Status (Code-Verified)
`MemoryDbContext` includes tables and mappings for:
- `EpisodicMemoryEvents`
- `SemanticClaims`
- `ClaimEvidence`
- `ClaimContradictions`
- `ConsolidationPromotions`
- `ToolInvocationAudits`
- `ProceduralRoutines`
- `SelfPreferences`

Migrations in `src/CognitiveMemory.Infrastructure/Persistence/Migrations` reflect incremental expansion from episodic through procedural/self/supersession/tool audits.

## Frontend Status (Code-Verified)
`frontend/cognitive-memory-chat` is a working Angular operations UI with:
- streaming chat integration (`/api/chat/stream`)
- episodic query actions
- semantic claim create/query actions
- procedural routine upsert actions
- self-model preference write/read actions
- consolidation and decay trigger actions
- post-response tool audit evidence display

## Runtime Configuration Snapshot
API defaults in `src/CognitiveMemory.Api/appsettings.json` and `appsettings.Development.json` show:
- Semantic Kernel provider defaulting to `Ollama`
- memory tooling enabled with environment-specific write policy
- consolidation worker enabled with periodic cadence
- decay worker enabled with periodic cadence

## Tests and Validation Status
Current test files include:
- `tests/CognitiveMemory.Application.Tests/ConsolidationServiceTests.cs` (2 tests)
- `tests/CognitiveMemory.Api.Tests/EndpointsTests.cs` (3 tests)

Observed local execution result (2026-02-21):
- `dotnet test CognitiveMemory.slnx --configuration Release --no-restore` fails because `Aspire.AppHost.Sdk/13.1.1` could not be resolved in this environment.
- This is a tooling/restore dependency issue, not a direct runtime behavior assertion for the memory APIs.

## Known Gaps / Risks
- Test coverage is currently narrow relative to endpoint surface (procedural, self-model, semantic supersession/decay, and tool-audit paths need deeper API + integration tests).
- No explicit auth/RBAC is wired for write endpoints yet.
- Current repository state contains a large in-progress refactor with many modified/deleted/new files; stabilization and documentation consolidation are still needed.

## Recommended Immediate Focus
1. Restore a clean, reproducible build/test path for Aspire SDK resolution in local and CI environments.
2. Add API integration tests for semantic supersede/decay, procedural query behavior, self-model preference lifecycle, and tool audit filtering.
3. Add authorization policy boundaries for mutation endpoints.
4. Publish a fresh architecture/API reference that matches the new endpoint set and folder structure.
