# CognitiveMemory Angular Chat UI

Angular chat client for the CognitiveMemory OpenAI-compatible API.

## What it talks to
- `GET /v1/models`
- `POST /v1/chat/completions`

The app is configured to call `/v1` by default.

## Local development
1. Start Aspire AppHost (includes frontend + API + Aspire YARP gateway):
   - `dotnet run --project CognitiveMemory.AppHost/CognitiveMemory.AppHost.csproj`
2. Open:
   - `http://localhost:8080`

The frontend process gets its listening port from Aspire via the `PORT` environment variable.
The direct exposed frontend endpoint from AppHost is `http://localhost:4210`.

## Standalone frontend mode (optional)
1. Install UI dependencies:
   - `cd frontend/cognitive-memory-chat`
   - `npm install`
2. Start Angular dev server:
   - `npm start`
3. Open:
   - `http://localhost:4200`

## Proxy behavior
`npm start` uses `proxy.conf.json`:
- `/v1` -> `http://localhost:8080`
- `/api` -> `http://localhost:8080`

This avoids browser CORS issues during local development.

## Notes
- Default API key in the UI is `local-dev-key`.
- You can override the base URL in the UI header if needed.
