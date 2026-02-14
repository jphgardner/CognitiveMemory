# Angular Chat UI

## Summary
This repository now includes an Angular chat application at:

- `frontend/cognitive-memory-chat`

The UI is now Tailwind-driven and optimized for runtime observability.

The UI targets the CognitiveMemory OpenAI-compatible facade:

- `GET /v1/models`
- `POST /v1/chat/completions`

## UI Architecture
- Chat panel (left): conversation controls, streaming messages, model/API selection.
- Observability panel (right): diagnostics and metadata for the selected assistant message.

Displayed API signals include:

- completion envelope (`id`, `model`, `created`, `finishReason`, stream mode, latency)
- usage tokens (`promptTokens`, `completionTokens`, `totalTokens`)
- confidence + conscience (`decision`, `riskScore`, `policyVersion`, `reasonCodes`)
- uncertainty flags
- tool execution telemetry (`toolName`, `source`, `ok`, `code`, `message`, `idempotencyKey`, `traceId`, `eventIds`, `resultCount`, `data`)
- citations and contradiction items
- raw metadata JSON

## Aspire YARP Integration
AppHost now uses Aspire's YARP integration to expose a gateway on port `8080`:

- `CognitiveMemory.AppHost/Program.cs`
- `builder.AddYarp("gateway").WithHostPort(8080)...`

Configured routes:

- `/v1/{**catch-all}` -> `api`
- `/api/{**catch-all}` -> `api`
- `/{**catch-all}` -> `frontend`

The frontend is added to AppHost using Aspire JavaScript hosting (`AddJavaScriptApp`) and is served under YARP.
The frontend endpoint is configured with `WithHttpEndpoint(port: 4210, env: "PORT")`, and the Angular start script reads `PORT` at runtime.

## Local Run Flow
1. Start AppHost (starts API, frontend, and YARP gateway):
   - `dotnet run --project CognitiveMemory.AppHost/CognitiveMemory.AppHost.csproj`
2. Open:
   - `http://localhost:8080`
3. Optional standalone frontend mode:
   - `cd frontend/cognitive-memory-chat`
   - `npm install`
   - `npm start`
   - then open `http://localhost:4200`

## Dev Proxy
Angular dev server proxy config:

- `frontend/cognitive-memory-chat/proxy.conf.json`

Routes forwarded to the YARP gateway (`http://localhost:8080`):

- `/v1`
- `/api`

This avoids CORS issues while keeping the browser-facing API path unchanged.

## Tailwind Setup
Tailwind is enabled through PostCSS:

- `frontend/cognitive-memory-chat/.postcssrc.json`
- `frontend/cognitive-memory-chat/package.json` (Tailwind/PostCSS dev dependencies)
- `frontend/cognitive-memory-chat/src/styles.css` (`@import "tailwindcss";` and typography/background baseline)
