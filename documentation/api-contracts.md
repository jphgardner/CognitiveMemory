# API Contracts
Version: 0.2
Updated: 2026-02-13

## Conventions
- Base path: `/api/v1`
- Content type: `application/json`
- Timestamps: ISO-8601 UTC
- IDs: UUID strings

## Error Shape
```json
{
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "topK must be between 1 and 50",
    "details": {}
  },
  "requestId": "5dbba9cc-2638-4cd5-a92b-13f58950fd8c"
}
```

## `POST /api/v1/ingest`
Ingest a source document for extraction and memory persistence.

Request:
```json
{
  "sourceType": "ChatTurn",
  "sourceRef": "conv:123/turn:5",
  "content": "We switched to SignalR.",
  "metadata": { "project": "PokemonMMO" }
}
```

Response `202 Accepted`:
```json
{
  "documentId": "6d6d9db2-d2f6-42ed-9b4a-11ca52e8b9d9",
  "status": "Queued"
}
```

## `POST /api/v1/query`
Retrieve ranked claims for a question.

Request:
```json
{
  "text": "What transport did we choose?",
  "filters": {
    "subject": "Project:PokemonMMO"
  },
  "topK": 10,
  "includeEvidence": true,
  "includeContradictions": true
}
```

Response `200 OK`:
```json
{
  "claims": [
    {
      "claimId": "8d9204f0-0951-48b7-8b12-218e8f687f0f",
      "predicate": "selected_transport",
      "literalValue": "SignalR",
      "score": 0.89,
      "confidence": 0.82,
      "evidence": [
        {
          "evidenceId": "f8ed96c0-7638-4054-96cb-0f7f6cf95fd3",
          "sourceType": "ChatTurn",
          "sourceRef": "conv:123/turn:5",
          "strength": 0.78
        }
      ],
      "contradictions": []
    }
  ],
  "meta": {
    "strategy": "hybrid",
    "latencyMs": 32,
    "requestId": "87f7b709-088d-45f5-b434-4fcb3f4a18f2"
  }
}
```

## `POST /api/v1/answer`
Return a debated and conscience-checked final answer.

Request:
```json
{
  "question": "What did we decide?",
  "context": {
    "project": "PokemonMMO"
  }
}
```

Response `200 OK`:
```json
{
  "answer": "You selected SignalR as the transport based on the recorded project decision.",
  "confidence": 0.82,
  "citations": [
    {
      "claimId": "8d9204f0-0951-48b7-8b12-218e8f687f0f",
      "evidenceId": "f8ed96c0-7638-4054-96cb-0f7f6cf95fd3"
    }
  ],
  "uncertaintyFlags": [],
  "contradictions": [],
  "conscience": {
    "decision": "Approve",
    "riskScore": 0.12,
    "policyVersion": "policy-2026-02-13"
  },
  "requestId": "2288e542-5269-40ec-ad10-7f2862c3004b"
}
```

## Validation Rules
- `topK` range: 1..50.
- `content` max size should be bounded by deployment policy.
- `includeEvidence=true` requires evidence payload availability.

## Conscience & Operations Endpoints
Read-model and observability endpoints:
- `GET /api/v1/conscience/decisions/recent`
- `GET /api/v1/conscience/decisions/{sourceType}/{sourceRef}`
- `GET /api/v1/conscience/replay/{requestId}`
- `GET /api/v1/conscience/policy`
- `GET /api/v1/ops/outbox/summary`
- `GET /api/v1/ops/outbox/events`
- `GET /api/v1/ops/calibration/summary`

## Entity Observability Endpoints
- `GET /api/v1/memory/entities?take=50`
- `GET /api/v1/memory/entities/{entityId}`

These endpoints expose the current canonical `MemoryEntity` rows created/upserted during ingest.

## OpenAI Chat `user` Field
For `POST /v1/chat/completions`, provide a stable `user` value so background ingest can bind claims to a stable subject identity across turns.

Example:
```json
{
  "model": "cognitivememory-chat",
  "user": "webchat-1234abcd",
  "stream": true,
  "messages": [
    { "role": "user", "content": "My name is James." }
  ]
}
```
