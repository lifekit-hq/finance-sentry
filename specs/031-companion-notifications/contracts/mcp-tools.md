# MCP Tool Contracts: Companion Notifications

Four new thin tools over CQRS handlers. Delivery/formatting/channel stay in the agent runtime — these tools only expose the FS-owned policy + event feed. All resolve the user from the authenticated MCP identity (`IIdentityResolver`), with an optional `userId` override like other tools.

## `get_notification_mode` (new)

**Request**: none (optional `userId`).
**Response**:
```json
{
  "mode": "scan",
  "quietHours": { "startLocal": 22, "endLocal": 7, "timeZone": "Europe/Dublin" },
  "maxProactivePerHour": 6,
  "digestHourLocal": 8,
  "updatedAt": "2026-07-22T09:00:00Z"
}
```
Returns the effective settings (defaults if no row yet). `mode` ∈ `quiet|digest|scan|realtime`.

## `set_notification_mode` (new)

**Request**:
| Param | Type | Required | Notes |
|---|---|---|---|
| `mode` | string | yes | `quiet|digest|scan|realtime` (case-insensitive) |
| `userId` | guid | no | defaults to MCP identity |

**Response**: `{ "mode": "realtime", "updatedAt": "..." }`. Invalid mode → rejected, previous mode unchanged (FR-005). Takes effect on the next event, no deploy (FR-003). This is how the agent flips the mode when Denys says "go quiet" / "realtime please."

## `get_pending_companion_events` (new)

**Request**:
| Param | Type | Required | Notes |
|---|---|---|---|
| `limit` | int | no | default 25, max 100 |
| `includeHeldForDigest` | bool | no | default false; the digest job uses true |

**Response**: events the agent has not yet delivered (disposition `Pending`/`Dispatched`, plus `HeldForDigest` when requested), newest first:
```json
{
  "events": [
    { "id": "…", "kind": "RiskViolation", "subject": "maxPositionWeight", "severity": "warning",
      "summary": "DRAM weight 47% exceeds 30% cap", "referenceId": "…", "occurredAt": "…", "disposition": "Pending" }
  ],
  "mode": "realtime",
  "retrievedAt": "…"
}
```
Read-only — does NOT mark delivered (explicit ack keeps at-least-once). Empty list is honest emptiness, never fabricated.

## `acknowledge_companion_events` (new)

**Request**: `{ "eventIds": ["…","…"] }` (the events the agent has now delivered to the user).
**Response**: `{ "acknowledged": 2 }`. Sets disposition → `Delivered` so they don't resurface (in the next pull, the scan, or the digest). Unknown/foreign ids are ignored.

## Payload: outbound agent wake (FS → runtime, not an MCP tool)

For `realtime` mode the dispatch relay POSTs to `Companion:AgentTriggerUrl` (if configured):
```json
{ "eventId": "…", "kind": "ThesisBreak", "subject": "MU", "severity": "critical", "occurredAt": "…" }
```
Headers: `Authorization: Bearer <Companion:AgentTriggerToken>` (runtime configuration, omitted when empty) and `Idempotency-Key: <eventId>` so the receiver dedups the relay's retries (up to `MaxDispatchAttempts`). The digest wake (`{ "kind": "Digest", "userId": "…", "count": n }`) carries the bearer header only.

No secrets, no full detail — the agent resolves specifics via the tools above using its own authenticated identity (FR-016). A missing URL ⇒ no push; the agent pulls instead.

Materiality note: `SyncFailure` is held for the digest in every mode except quiet unless the referenced bank account has had no successful sync for more than 24h (`MaterialityPolicy.SyncFailureEscalationAge`), in which case the mode disposition applies. Provider-level sync failures with no account reference are always held.
