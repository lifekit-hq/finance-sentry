# Research: Companion Notification Modes + Event-Driven Push

## R1. Reliable dispatch — a full event bus, or a focused outbox?

**Decision**: A **focused outbound outbox** owned by the Companion module (`companion_events` + a Hangfire relay), NOT a full event bus (the roadmap's `026-event-bus-outbox`, since dropped).
**Rationale**: 026 is a large, unimplemented in-monolith *pub/sub* infrastructure (broker + cross-module rewiring). 031 needs reliable *outbound* delivery for one flow (material event → wake agent) with no-loss/retry/dedup — a tiny fraction of 026, and 026 is *internal* so it wouldn't even provide the outbound hop. The focused outbox borrows 026's transactional-outbox pattern and is a deliberate stepping-stone.
**Alternatives considered**: (a) wait for 026 — rejected, blocks a small feature behind large infra; (b) fire-and-forget HTTP from the detector — rejected, loses events on failure and has no mode gating.
**Migration path**: none planned — 026 was dropped on 2026-11-15, so the polling capture and the outbound relay stay as built.

## R2. Event capture — hook the detectors, or poll?

**Decision**: **Poll** existing tables on a frequent Hangfire job, reading through thin read contracts (`IMaterialAlertReader`, `IThesisBreakReader`, `IAnalystActionFeedReader`) placed in `Core.Interfaces` and implemented by the source modules. Each read is "give me items newer than a stored watermark."
**Rationale**: Non-invasive to the detector code paths, keeps module coupling to declared contracts (constitution I), and puts materiality/dedup in one owned place (FS owns policy). Alerts are already persisted with `CreatedAt`; thesis breaks and analyst actions likewise — a watermark scan is simple and idempotent.
**Alternatives considered**: (a) in-process events from each detector — more invasive, and there is no bus yet (that's 026); (b) reading other modules' `DbContext` directly — violates module isolation.
**Latency**: capture cadence is ≤60s so realtime dispatch meets SC-002. A held-name filter (via existing `IBrokerageHoldingsReader`) gates analyst actions.

## R3. Materiality + dedup

**Decision**: Materiality is a small owned policy: alerts (any generated risk/sync/spend/opportunity alert is already "material"), thesis-invalidation breaks, and analyst actions **on held tickers** above a bar (upgrade/downgrade/initiate, or a target change). Dedup uses a **logical dedup key** per event (e.g. `kind:subject:day` or the source row id) so re-detection of the same logical event collapses to one `companion_events` row (unique index on the key).
**Rationale**: Reuses detectors that already encode "worth alerting"; the dedup key is the same idea proven in 030's analyst-action dedup and the alerts module's `HasRecentAsync`.
**Alternatives considered**: a numeric materiality score — rejected as false precision for v1; the sources already gate materiality.

## R4. Mode → disposition mapping

**Decision**: On capture, each event's disposition is computed from the current mode:

| Mode | Disposition written | Proactive outreach |
|---|---|---|
| `quiet` | `SuppressedByMode` | none (recorded only) |
| `digest` | `HeldForDigest` | consolidated once/day |
| `scan` | `Pending` | surfaced on the existing periodic scan (agent pull) |
| `realtime` | `Pending` (+ flagged for immediate dispatch) | webhook wake now |

**Rationale**: One field drives everything; the mode is read per-capture so a switch takes effect on the next event (FR-003). Quiet-hours + rate-limit are applied at *dispatch* time (realtime), producing `DeferredQuietHours` / `SuppressedByRateLimit`, so nothing is lost (FR-007/FR-013).

## R5. The outbound wake (FS → agent runtime)

**Decision**: A configurable **outbound webhook**. The dispatch relay POSTs a minimal JSON payload (`eventId`, `kind`, `subject`, `severity`, `occurredAt` — **no secrets, no full detail**) to `Companion:AgentTriggerUrl`. If the URL is unset, dispatch is a no-op and the event stays `Pending` for the agent to **pull via MCP** (`get_pending_companion_events`). Failed POSTs increment a retry count and are re-attempted by the next relay tick (bounded), never dropped (FR-014).
**Rationale**: Keeps the one external integration behind a single config value; the FS side is fully correct and testable regardless of whether the OpenClaw trigger endpoint is wired. Pull-fallback makes US1 (modes) + US3 (digest) fully shippable with zero outbound wiring; realtime push is the only piece gated on the URL.
**Open item (runtime side, out of scope)**: the OpenClaw endpoint that receives the POST and runs an agent turn (`openclaw agent` / `system event`). Tracked for the agent runtime, not this feature.

## R6. Module placement

**Decision**: New `FinanceSentry.Modules.Companion` with its own `CompanionDbContext` (schema `companion`), design-time factory, and migration — mirrors the `Risk` module.
**Rationale**: Notification policy is cohesive and shouldn't bloat Research/Alerts; a dedicated schema keeps migrations independent (constitution: isolated modules). Precedent: Radar, Risk, CryptoSync, BrokerageSync each own a context.

## R7. MCP surface

**Decision**: Four thin tools over CQRS handlers: `get_notification_mode`, `set_notification_mode`, `get_pending_companion_events`, `acknowledge_companion_events`. The agent reads/flips the mode and pulls+acks events; **it never sends through FS** — delivery stays in the runtime.
**Rationale**: Matches the existing thin-tool pattern (030). Explicit ack (rather than auto-mark-on-read) preserves at-least-once from the agent's side so an agent crash mid-delivery doesn't lose the event.

## R8. Scheduling

**Decision**: Three Hangfire jobs — `companion-capture` (every 1 min), `companion-dispatch` (every 1 min, realtime relay + retries), `companion-digest` (daily, `digest` mode only). All `[DisableConcurrentExecution]`. Capture and dispatch can be one job ordered capture→dispatch to simplify; kept separate for isolation.
**Rationale**: 1-min cadence meets the 60s realtime target without a broker; low event volume makes polling cheap. Digest daily at a fixed local hour.
