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
| `includeHeldForDigest` | bool | no | default false; only a Digest wake sets true, and it is honoured only with `heldOverrideReason` |
| `heldOverrideReason` | string | with `includeHeldForDigest` | why held events are pulled (the Digest wake payload supplies `"daily digest"`); logged. Without it held events stay excluded and the result carries a `note` |

**Response**: events the agent has not yet delivered (disposition `Pending`/`Dispatched`, plus `HeldForDigest` when requested), newest first:
```json
{
  "events": [
    { "id": "…", "kind": "RiskViolation", "subject": "maxPositionWeight", "severity": "warning",
      "summary": "DRAM weight 47% exceeds 30% cap", "referenceId": "…", "occurredAt": "…", "disposition": "Pending" }
  ],
  "mode": "realtime",
  "retrievedAt": "…",
  "note": null
}
```
`note` is set only when `includeHeldForDigest` was requested without a `heldOverrideReason` (held events withheld; an empty list then does not mean none exist). Read-only — does NOT mark delivered (explicit ack keeps at-least-once). Empty list is honest emptiness, never fabricated.

## `acknowledge_companion_events` (new)

**Request**: `{ "eventIds": ["…","…"] }` (the events the agent has now delivered to the user).
**Response**: `{ "acknowledged": 2 }`. Sets disposition → `Delivered` so they don't resurface (in the next pull, the scan, or the digest). Unknown/foreign ids are ignored.

## Payload: outbound agent wake (FS → runtime, not an MCP tool)

For `realtime` mode the dispatch relay POSTs to `Companion:AgentTriggerUrl` (if configured):
```json
{ "eventId": "…", "userId": "…", "kind": "ThesisBreak", "subject": "MU", "severity": "critical", "occurredAt": "…", "appUrl": "https://app.example.com/assets/MU" }
```
`appUrl` (#466 N-B) is the absolute link to the app page the event is about (`Companion:PublicBaseUrl` + the event's `AppPath`, see `CompanionAppUrl.For`). The key is omitted, never null or relative, when no public base URL is configured or the event has no target page. The same field is on each event returned by `get_pending_companion_events` (`docs/mcp.md`).
Headers: `Authorization: Bearer <Companion:AgentTriggerToken>` (runtime configuration, omitted when empty) and `Idempotency-Key: <eventId>` so the receiver dedups the relay's retries (up to `MaxDispatchAttempts`). The digest wake (`{ "kind": "Digest", "userId": "…", "count": n, "includeHeldForDigest": true, "heldOverrideReason": "daily digest" }`) carries the bearer header only.

No secrets, no full detail — the agent resolves specifics via the tools above using its own authenticated identity (FR-016). A missing URL ⇒ no push; the agent pulls instead.

AI-permission-gated: the agent runtime serves only accounts holding `ai.use` (Owner role or a per-person grant; checked via `IUserAuthorizationChecker`). Events (and digest wakes) for any other user are never posted — the dispatch job marks those events `SuppressedNonOwner` (terminal) so they leave the realtime batch.

Materiality note: `SyncFailure` is held for the digest in every mode except quiet unless the referenced bank account has had no successful sync for more than 24h (`MaterialityPolicy.SyncFailureEscalationAge`), in which case the mode disposition applies. Provider-level sync failures with no account reference are always held. That is the capture-time decision only: every delivery read (this tool, the dispatch relay, the digest) first runs the SyncFailure reconciliation — an event whose alert has since closed is expired and never returned, and a held one whose alert is still open past the escalation age is escalated to `Pending` (`data-model.md`, "SyncFailure reconciliation at delivery time").
