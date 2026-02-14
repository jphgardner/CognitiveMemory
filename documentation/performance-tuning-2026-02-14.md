# Performance Tuning (2026-02-14)
Date: 2026-02-14

## Why
Observed runtime behavior showed:

- Chat response path spending extra time on memory prefetch.
- Recall querying too many claims per turn.
- High background load from ingesting both user and assistant turns.
- Conscience worker processing every chat-origin claim.

## Optimizations Applied

### 1) Bounded Recall Candidate Set
`memory_recall.search_claims` now limits repository candidate fetch size based on requested `topK`.

- File: `src/CognitiveMemory.Application/AI/Plugins/MemoryRecallPlugin.cs`
- Candidate budget: `topK * 12`, clamped to `24..180`

Repository support added:

- File: `src/CognitiveMemory.Application/Interfaces/IClaimRepository.cs`
- File: `src/CognitiveMemory.Infrastructure/Repositories/ClaimRepository.cs`
- `GetQueryCandidatesAsync(..., maxCandidates = 0)` now supports bounded result sets.
- Read paths use `AsNoTracking()` to reduce EF tracking overhead for recall queries.

### 2) Time-Bounded Prefetch Recall in Chat Path
Prefetch recall for chat runs with a short timeout and bounded expansion.

- File: `src/CognitiveMemory.Api/Endpoints/OpenAiCompatEndpoints.cs`
- Timeout: `900ms`
- Prefetch `topK`: `4`

Prefetch now always executes (for grounded continuity) while remaining latency-bounded.

### 3) Remove Queue-Based Chat Ingest
Chat-turn ingest queue was removed from the `/v1/chat/completions` path.

- Removed:
  - `src/CognitiveMemory.Api/Background/ChatIngestBackgroundQueue.cs`
  - `src/CognitiveMemory.Api/Background/ChatIngestQueueOptions.cs`
  - `src/CognitiveMemory.Api/Background/ChatIngestWorkItem.cs`
  - `src/CognitiveMemory.Api/Background/IChatIngestQueue.cs`
- Added:
  - `src/CognitiveMemory.Api/Configuration/ChatPersistenceOptions.cs`
- Updated:
  - `src/CognitiveMemory.Api/Endpoints/OpenAiCompatEndpoints.cs`

Chat memory persistence now happens via memory tools (`memory_write.ingest_note` / `memory_write.create_claim`) so background work is hooked from tool-emitted outbox events.

### 4) Move Heavy Processing to Background Pipeline
Tool-based note writes emit `memory.document.ingested` and background worker now performs extraction/entity/claim materialization from that event.

- File: `src/CognitiveMemory.Api/Background/ConscienceOutboxWorker.cs`
- File: `src/CognitiveMemory.Application/Services/DocumentIngestionPipeline.cs`

This keeps `/v1/chat/completions` focused on chat latency while heavy memory side-effects run asynchronously.

### 5) Reduce Redis Log Noise
Set Redis category log level to warning in app settings.

- File: `src/CognitiveMemory.Api/appsettings.json`
- File: `src/CognitiveMemory.Api/appsettings.Development.json`
- `Logging:LogLevel:StackExchange.Redis = Warning`

## Expected Impact
- Lower p95 chat latency on memory-enabled turns.
- Lower database and background worker pressure.
- Fewer unnecessary LLM-side background analyses.
- Cleaner logs for runtime diagnosis.
