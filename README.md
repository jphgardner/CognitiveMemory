# CognitiveMemory

Aspire-based scaffold for the Cognitive Memory System using Clean Architecture.

## Prerequisites
- .NET 10 SDK (`10.0.100+`)
- Docker (or compatible local container runtime)
- Node.js + npm (required for the Angular frontend resource in AppHost)

Optional (for `aspire` CLI commands):

```bash
curl -sSL https://aspire.dev/install.sh | bash
```

## First-time setup
```bash
dotnet restore CognitiveMemory.slnx
```

## Run
From the repo root:

```bash
dotnet run --project CognitiveMemory.AppHost/CognitiveMemory.AppHost.csproj
```

## Architecture
- `src/CognitiveMemory.Domain`: core entities and enums
- `src/CognitiveMemory.Application`: use-case contracts, ports, and orchestrating services
- `src/CognitiveMemory.Infrastructure`: EF Core + Redis adapters and dependency wiring
- `src/CognitiveMemory.Api`: HTTP transport layer only
- `CognitiveMemory.ServiceDefaults`: shared resilience, telemetry, health endpoints
- `CognitiveMemory.AppHost`: Aspire orchestration for resources and services

## Aspire Resources
- PostgreSQL (`memorydb`)
- Redis (`cache`)
- Angular Frontend (`frontend`)
- YARP Gateway (`gateway`, host port `8080`)

Runtime alignment:
- API resolves EF Core `MemoryDbContext` via Aspire PostgreSQL reference `memorydb`.
- Query result cache uses Aspire Redis reference `cache` (configurable TTL via `QueryCache:TtlSeconds`).
- On API startup, pending EF Core migrations are applied automatically; if no migrations exist yet, startup falls back to `EnsureCreated()`.
- Development Semantic Kernel provider is configured for local Ollama (`SemanticKernel:Provider=Ollama`).

## Current Endpoints
- Auth: `/api/auth/*`
- Companions: `/api/companions/*`
- Chat: `/api/chat` and `/api/chat/stream`
- Episodic memory: `/api/episodic/*`
- Semantic memory: `/api/semantic/*`
- Procedural memory: `/api/procedural/*`
- Self model: `/api/self-model/*`
- Relationships: `/api/relationships/*`
- Eventing/SSE: `/api/eventing/*`
- Subconscious debates: `/api/subconscious/*`
- Scheduled actions: `/api/scheduled-actions/*`
- Workspace and operations: `/api/workspace/*`, `/api/tool-invocations/*`
- Cognitive control endpoints: `/api/consolidation/*`, `/api/reasoning/*`, `/api/planning/*`, `/api/identity/*`, `/api/truth/*`

## OpenAI-Compatible Facade
- `GET /v1/models`
- `GET /v1/models/{id}`
- `POST /v1/chat/completions`
- `POST /v1/embeddings`

For OpenAI-client chat apps:
- Base URL: `http://localhost:8080/v1`
- API key: any non-empty bearer token (OpenAI-compatible facade validates non-empty bearer token)
- Model: `cognitivememory-chat`
- Embeddings model: `cognitivememory-embedding`

For the bundled Angular chat UI:
- Source: `frontend/cognitive-memory-chat`
- Routed through YARP at: `http://localhost:8080/`
- AppHost starts the frontend via Aspire JavaScript integration (`AddJavaScriptApp`) using `WithHttpEndpoint(port: 4210, env: "PORT")`
- UI default API key: `local-dev-key`

## Documentation
- `PRODUCT_DEEP_DIVE.md`
- `EVENT_DRIVEN_ARCHITECTURE.md`
- `SUBCONSCIOUS_DEBATE_BLUEPRINT.md`
- `CODEBASE_FULL_STATUS.md`
