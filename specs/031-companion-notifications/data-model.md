# Data Model: Companion Notification Modes + Event-Driven Push

New context: **`CompanionDbContext`** — schema `companion`, history table `__ef_migrations_history_companion`. Migration `M001_InitialSchema` (with `.Designer.cs`).

## Enums (stored as string)

- **`NotificationMode`**: `Quiet | Digest | Scan | Realtime`. Default `Scan`.
- **`CompanionEventKind`**: `RiskViolation | SyncFailure | UnusualSpend | Opportunity | ThesisBreak | AnalystAction`.
- **`EventDisposition`**: `Pending | Dispatched | HeldForDigest | Delivered | SuppressedByMode | SuppressedByDedup | SuppressedByRateLimit | SuppressedNonOwner | DeferredQuietHours | Failed | Expired`.

## Entity: `CompanionNotificationSetting` (table `companion_notification_settings`)

One row per user (the user's proactivity dial + guardrails).

| Field | Type | Notes |
|---|---|---|
| `Id` | Guid (PK) | |
| `UserId` | Guid | **unique**; per-user isolation |
| `Mode` | NotificationMode (string) | default `Scan` |
| `QuietHoursStartLocal` | int? | hour 0–23 in the user's tz; null = no quiet hours |
| `QuietHoursEndLocal` | int? | hour 0–23 |
| `TimeZoneId` | string? | IANA tz for quiet-hours + digest; default from config |
| `MaxProactivePerHour` | int | rate-limit cap; default from config |
| `DigestHourLocal` | int | daily digest hour; default from config |
| `UpdatedAt` | DateTimeOffset | |

Validation: `Mode` must parse to the enum (FR-005); quiet-hours are optional; a missing row means defaults (mode `Scan`) — created lazily on first set/read.

**Provisioning (issue #686)**: a row is created eagerly, not only lazily, when a user is provisioned — `RegisterCommandHandler` and `VerifyGoogleCredentialCommandHandler` (Auth) publish `UserRegisteredEvent` (`FinanceSentry.Core.Cqrs`) after creating the `ApplicationUser`; Companion's `UserRegisteredSettingsProvisioningHandler` reacts by upserting the default row (`Mode=Scan`, defaults from `CompanionOptions`). This is best-effort (publish failures never fail registration) and exists so the policy path (quiet hours, per-hour cap, digest) has a real row to read from day one instead of depending on `GetOrDefaultAsync`'s unsaved default, which the digest job cannot iterate. Migration `M002_NotificationSettingsProvisioning` backfills the same default row for every pre-existing user.

**Digest job gating**: `CompanionDigestJob` iterates users who currently have at least one `HeldForDigest` event (`ICompanionEventRepository.ListHeldForDigestUserIdsAsync`), not users with a persisted `Mode=Digest` row — `MaterialityPolicy` holds some kinds (e.g. `SyncFailure`) for the digest regardless of mode, so gating on mode alone stranded them. The settings row is used only for `TimeZoneId`/`DigestHourLocal` timing. The held list is passed through the SyncFailure reconciliation below before counting, so a user whose held events all expire gets no digest wake.

**Stranded events (issue #686 migration)**: events already `HeldForDigest` before this fix shipped predate any scheduled delivery path and could be stale (sync failures from weeks/months prior). `M002_NotificationSettingsProvisioning` explicitly transitions them to the terminal disposition `Expired` (with a `LastError` note) rather than letting them silently deliver on the first post-deploy digest tick.

**SyncFailure reconciliation at delivery time**: capture decides a `SyncFailure`'s disposition once, from the account's staleness at that moment, and the event then waits for the digest hour while the alert it mirrors usually clears within hours (62 of 63 relays went out already resolved; the Monobank outage arrived 3 days late). `ISyncFailureReconciler` (`SyncFailureReconciler`) therefore runs on every read that leads to delivery — `get_pending_companion_events`, `CompanionDispatchJob` and `CompanionDigestJob` — over the `Pending`/held `SyncFailure` events whose `DedupKey` names an alert (`alert:<id>`): an event whose alert is no longer open (`IMaterialAlertReader.GetOpenIdsAsync` — the row exists, is not dismissed and has no `ResolvedAt`; a row deleted with its account counts as closed) is set `Expired` (`LastError` "alert no longer open before delivery") and dropped from the result; a `HeldForDigest` event whose alert is still open is re-evaluated through `MaterialityPolicy.DispositionFor` with the alert's age (`now - OccurredAt`, the length of the outage) and becomes `Pending` only when the policy says so (open past `SyncFailureEscalationAge`, mode not Quiet) — any other answer leaves it held, so Quiet-mode held events are not silenced. Other kinds pass through untouched. Pinned by `SyncFailureReconcilerTests` (Companion tests) and `MaterialAlertReaderOpenIdsTests` (integration).

## Entity: `CompanionEvent` (table `companion_events`)

The outbox row — one captured material event and its lifecycle.

| Field | Type | Notes |
|---|---|---|
| `Id` | Guid (PK) | |
| `UserId` | Guid | indexed |
| `Kind` | CompanionEventKind (string) | |
| `Subject` | string | e.g. ticker or rule key (max 128) |
| `Severity` | string | mirrors source severity (`info`/`warning`/`critical`) |
| `Summary` | string | short human line for the agent (max 500) |
| `DedupKey` | string | logical identity; **unique** — collapses re-detection (max 200) |
| `ReferenceId` | Guid? | source row (alert id / thesis id / analyst action id) |
| `SourceModule` | string | `alerts` / `research` |
| `Disposition` | EventDisposition (string) | indexed; drives the relay + digest |
| `OccurredAt` | DateTimeOffset | source event time |
| `CapturedAt` | DateTimeOffset | when the capture job wrote it |
| `DispatchedAt` | DateTimeOffset? | when the wake was sent |
| `DeliveredAt` | DateTimeOffset? | when the agent acked delivery |
| `Attempts` | int | dispatch retry counter |
| `LastError` | string? | last dispatch failure reason |

Indexes: unique `DedupKey`; `(UserId, Disposition, OccurredAt)` for the relay/digest/pull queries; `(UserId, CapturedAt)`.

**State transitions** (disposition):
```
capture ──► Pending ───────────(realtime relay)──► Dispatched ──(agent ack)──► Delivered
        ├─► HeldForDigest ─────(daily digest)────► Delivered
        │        └─(SyncFailure, alert open > escalation age at digest/pull)─► Pending
        ├─► Expired                 (terminal; a SyncFailure whose alert closed before delivery — reconciled on every
        │                            pull/relay/digest read — or a pre-#686 stranded held event, see above)
        ├─► SuppressedByMode        (quiet — terminal)
        ├─► SuppressedByDedup       (terminal; never actually inserted — the unique key rejects it)
        ├─► SuppressedByRateLimit / DeferredQuietHours  (realtime, re-evaluated next tick)
        ├─► SuppressedNonOwner      (realtime relay, user lacks `ai.use` — terminal; name predates permissions)
        └─► Failed                  (retry-exhausted; visible, re-drivable)
```
Every captured event is recorded with a disposition — none lost (FR-007 / SC-005).

## Watermark

Capture progress is tracked per source so the poll reads only new rows. Stored either as a tiny `companion_capture_state` row (source → last-seen timestamp) or derived from `MAX(OccurredAt)` per source in `companion_events`. **Decision**: a small `companion_capture_state` table (`Source` PK, `Watermark` timestamp) — explicit, survives purges of `companion_events`.

## Cross-module read contracts (in `FinanceSentry.Core.Interfaces`)

- `IMaterialAlertReader.GetNewSinceAsync(watermark, ct)` → alert rows (id, userId, type, severity, title, createdAt) — implemented by **Alerts**.
- `IThesisBreakReader.GetNewBreaksSinceAsync(watermark, ct)` → thesis-break rows — implemented by **Research**.
- `IAnalystActionFeedReader.GetNewSinceAsync(watermark, ct)` → analyst actions — implemented by **Research**; the capture service filters to held tickers via existing `IBrokerageHoldingsReader`.
