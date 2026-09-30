# Quickstart / Verification: Companion Notifications

Backend + MCP only. Run against the Docker stack.

## Prereqs
- `docker compose -f docker/docker-compose.dev.yml up -d --build postgres api`
- Health: `GET http://localhost:5001/api/v1/health` → healthy
- Confirm migration applied: `M001_InitialSchema` in `__ef_migrations_history_companion`, and tables `companion.companion_notification_settings`, `companion.companion_events`, `companion.companion_capture_state` exist.

## US1 — mode control (MVP, pull-based)
1. `get_notification_mode` → defaults (`mode: scan`).
2. `set_notification_mode {"mode":"quiet"}` → persisted; `get_notification_mode` reflects it.
3. `set_notification_mode {"mode":"bogus"}` → rejected, mode unchanged.
4. Confirm the change needed no redeploy (SC-001) and on-demand queries still answer in every mode (FR-004).

## US2 — capture + realtime dispatch
1. Set mode `realtime`. Trigger a material event (e.g. run the risk check to raise a violation, or seed an analyst action on a held ticker).
2. Within ~1 min the capture job writes a `companion_events` row (`Pending`), and the dispatch relay POSTs the wake to `Companion:AgentTriggerUrl` (if set) → row `Dispatched`; `DispatchedAt` set (SC-002 ≤60s).
3. With no `AgentTriggerUrl`: row stays `Pending`; `get_pending_companion_events` returns it (pull path).
4. `acknowledge_companion_events {"eventIds":[...]}` → `Delivered`; it no longer appears in a subsequent pull (SC-005 no repeats).
5. Set mode `quiet`; trigger the same event → row written `SuppressedByMode`, **no** dispatch, and it does not appear in the pull (SC-003).
6. Fire the same logical event twice → one row only (unique `DedupKey`, SC-004).
7. Point `AgentTriggerUrl` at an unreachable host in realtime → `Attempts` increments, `LastError` set, event retried not lost; after the limit → `Failed` (visible), never dropped (FR-014).

## US3 — daily digest
1. Set mode `digest`. Trigger several material events → all written `HeldForDigest`; none dispatched immediately.
2. Run `companion-digest` (or wait for the daily hour) → `get_pending_companion_events {"includeHeldForDigest":true,"heldOverrideReason":"daily digest"}` (the Digest wake payload carries both fields; without the reason held events stay excluded) returns the day's set once; after the agent delivers + acks, they are `Delivered` and don't repeat next day (SC-006).
3. A digest run with no held events → no forced empty message.

## Boundary check
- Confirm FS sends nothing to Telegram/email itself — the only outbound is the POST to `AgentTriggerUrl` carrying ids/refs (no secrets, FR-015/FR-016). All user-facing delivery is the agent's.
