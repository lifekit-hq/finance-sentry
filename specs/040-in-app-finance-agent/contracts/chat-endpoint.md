# Contract: Agent chat endpoint

Authenticated (JWT) and **Owner-only** (`RequireOwner` policy; the asset-dossier narrative routes too). All routes scoped to the caller's user; no user id in the path/body is honored.

## POST `/api/v1/agent/chat` — send a message, stream the reply (SSE)

**Request** (JSON):
```json
{ "conversationId": "uuid | null", "message": "why is my book down today?" }
```
- `conversationId` null → a new conversation is created (title derived from the message).

**Response**: `text/event-stream` (SSE). Event types (each `data:` is JSON):

| event | data | meaning |
|---|---|---|
| `conversation` | `{ "conversationId": "uuid" }` | sent first; the (new or existing) conversation id |
| `text` | `{ "delta": "…" }` | assistant token delta — append in order |
| `tool` | `{ "name": "get_portfolio_snapshot", "phase": "start\|end" }` | a tool call started/finished — drives progressive feedback (SC-007); **no raw tool payloads streamed** |
| `error` | `{ "code": "…", "message": "…" }` | recoverable error (e.g. `agent_not_configured`, `llm_unavailable`); stream then ends |
| `done` | `{ "messageId": "uuid" }` | final assistant message persisted; stream closes |

**Behavior**:
- Persists the user message, runs the tool-use loop (compose persona → call model with bridged tools → dispatch `tool_use` in the caller's scope → iterate to a final answer), streaming as it goes; persists the assistant message on `done`.
- **Keyless** (`Agent__Anthropic__ApiKey` unset) → single `error` event `agent_not_configured`, no model call.
- **Unauthenticated** → 401 before any streaming/model call.
- **Signed in but not Owner** → 403 `{ "error": "…", "errorCode": "FORBIDDEN" }` before any streaming/model call; the agent runtime is never invoked.
- Tier-3: the model has no money/trade/credential tool; such requests yield a drafted answer that escalates, never an action.

## GET `/api/v1/agent/conversations` — list the caller's conversations
Returns `[{ id, title, updatedAt, modelId }]` ordered by `updatedAt desc`.

## GET `/api/v1/agent/conversations/{id}` — full history
Returns the conversation + ordered messages (role, content, tool metadata). 404 if not owned by the caller (never leaks another user's thread).

## DELETE `/api/v1/agent/conversations/{id}` — delete a thread (cascade messages). Owner-only.

### Errors
Standard app error envelope with `errorCode`; new codes (`agent_not_configured`, `llm_unavailable`, `conversation_not_found`) added to the frontend error-message registry in the same PR (Principle VI.3).
