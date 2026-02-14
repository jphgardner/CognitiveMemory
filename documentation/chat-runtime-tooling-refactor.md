# Chat Runtime Tooling Refactor
Date: 2026-02-14

## What Changed
The OpenAI-compatible chat runtime now captures and returns real tool execution telemetry for each response.

Updated file:

- `src/CognitiveMemory.Api/Endpoints/OpenAiCompatEndpoints.cs`

## Runtime Flow (Per Chat Request)
1. Build conversation/user identity keys (`chat-user:{conversationKey}`).
2. Run prefetch recall (`memory_recall.search_claims` + top claim/evidence expansion) scoped to the user actor key.
3. Feed preloaded recall results into the chat agent prompt context.
4. Run the Semantic Kernel chat agent with tracked plugin wrappers.
5. Persist user/assistant notes using memory tools (`memory_write.ingest_note`) and durable-fact fallback (`memory_write.create_claim`) when needed.
6. Return response metadata including:
   - `confidence`
   - `uncertaintyFlags`
   - `conscience`
   - `toolExecutions` (new, populated from actual tool envelopes)

## Tool Execution Metadata
`metadata.toolExecutions[]` now includes:

- `toolName`
- `source`
- `ok`
- `code`
- `message`
- `idempotencyKey`
- `traceId`
- `data` (raw tool envelope data payload)
- `eventIds`
- `resultCount`

## Memory Filtering
Memory recall now supports a filtered variant:

- `memory_recall.search_claims_filtered`

Filters:
- `subjectFilter`
- `predicateFilter`
- `literalContains`
- `sourceTypeFilter`
- `minConfidence`
- `minScore`
- `scopeContains`

## Prompt Source Of Truth
All runtime prompts are now centralized under the repository root `prompts/` folder and loaded through `PromptLoader`:

- `prompts/chat/agent-system/prompt.md`
- `prompts/claim-extraction/prompt.md`
- `prompts/conscience/analyzer/prompt.md`
- `prompts/debate/*/prompt.md`

## Frontend Update
Angular chat UI now renders a richer observability panel with:

- completion diagnostics
- conscience/uncertainty metadata
- tool execution cards
- raw `tool.data` JSON payloads
- citations and contradiction lists
- raw metadata JSON

Updated files:

- `frontend/cognitive-memory-chat/src/app/app.ts`
- `frontend/cognitive-memory-chat/src/app/app.html`

## Chat Agent Prompt and Tooling Policy Update
The chat agent runtime now reinforces memory-first behavior when uncertain:

- prompt explicitly requires `memory_recall.search_claims` before uncertain/"don't know" replies
- prompt instructs follow-up `memory_recall.get_claim` and `memory_recall.get_evidence` on top hits
- prefetch now expands top claims by calling `get_claim` and `get_evidence` (source marked as `prefetch`)

Additionally, development config enables safe write tools so agent memory write calls are allowed:

- `src/CognitiveMemory.Api/appsettings.Development.json` -> `AgentTooling:Mode = ReadWriteSafe`

## Refactor: Tool-Managed Chat Memory
Queue-based chat ingest has been removed from the chat completion path.

New behavior:

- in `AgentOnly` mode, chat memory writes are agent-owned only (no system post-turn write injection)
- in `HybridFallback`/`SystemPostTurn` modes, system post-turn note ingest uses `memory_write.ingest_note`
- durable personal-name facts can be persisted through `memory_write.create_claim` fallback in non-`AgentOnly` modes
- outbox worker processes `memory.document.ingested` to run extraction/entity/claim materialization in the background
- conscience/calibration processing remains outbox-driven and runs after claim events are emitted

## Plan-Alignment Policy Modes
- `ChatPersistence.Mode`
  - `AgentOnly`: agent-owned writes only (no system post-turn ingest/fallback)
  - `HybridFallback`: system post-turn ingest/fallback only when agent produced no successful write
  - `SystemPostTurn`: always run system post-turn ingest path
- `AgentTooling.Mode`
  - `ReadOnly`
  - `ReadWriteSafe`
  - `Privileged` (required for governance mutations)
