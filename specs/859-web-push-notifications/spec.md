# Feature Specification: Web Push Notifications

**Feature Branch**: `feat/859-web-push-notifications`

**Created**: 2026-10-06

**Status**: Draft

**GitHub Issue**: #859

**Amends**: [031 Companion Notifications](../031-companion-notifications/spec.md) — FR-015 and SC-007

## Context

The app is installable as a PWA (#791) and its service worker (`ngsw-worker.js`) already knows how to show a
notification from a `{"notification": {…}}` payload and route a click to a URL. What is missing is a sender.

Today an alert reaches a person only through Ledger: the Companion outbox (`companion.companion_events`)
records a material event, and Ledger pulls it on its next scan (every two hours) and decides whether to message
on Telegram. Agent wake-ups are off in production (no `Companion__AgentTriggerUrl`), and agent dispatch is gated
on `ai.use` and on Realtime mode, so most users and most modes get no real-time path at all.

Spec 031 set the boundary "Finance Sentry owns policy, the agent runtime owns delivery" and stated that FS adds
no user-facing channel. That boundary was right for the agent path; it is too broad for a channel that is
the user's own installed app and carries no agent cost. This spec narrows it.

## Amendment to 031

| 031 clause | Before | After |
|---|---|---|
| FR-015 | FS MUST NOT deliver to any user-facing channel itself. | On the **agent path**, FS still MUST NOT deliver itself; the agent runtime owns delivery, formatting and channel. FS **additionally owns a Web Push channel** to the signed-in user's own installed app. |
| SC-007 | FS adds no new outbound user-facing channel. | The **agent path** adds no new outbound channel. Web Push is a separate, parallel channel and is not part of that path. |

Everything else in 031 is unchanged, in particular: the notification mode, dispatch gates, `Disposition`
mutation, quiet-hours semantics, and FR-016 (no secrets or sensitive detail in the dispatch payload).

## Decisions

1. **Independent of Ledger's notification mode.** Push has its own opt-in. Setting Ledger to Quiet, Digest or
   Scan does not silence the phone, and push never reads or writes `Disposition`.
2. **Headline only on the lock screen.** The notification shows the kind and the subject (for example
   "Low balance · Monobank UAH"). No amounts, merchants or P&L. Detail appears after the tap, inside the
   authenticated app. This matches FR-016's posture.
3. **Everyone signed in gets push for their own alerts.** `OperationalFailure` goes to `ops.admin` holders
   only. `ai.use` does not gate push: it exists to protect agent spend and push has none.
4. **Push and Telegram both fire, independently.** The same material event can reach the phone through push
   and Telegram through Ledger's next scan. Neither suppresses the other.

## User Scenarios

### [US1] Subscribe a device

A signed-in user turns push on from a user gesture in the installed app, grants the browser permission, and
the device's subscription is stored against their account.

**Acceptance**
- No subscription is created without an explicit user action.
- A subscription belongs to exactly one user and is visible only to them.
- A keyless deployment (no VAPID configuration) reports push as unavailable.

### [US2] Receive an alert on the lock screen

A material event is recorded in the Companion outbox. A notification appears on each of the user's subscribed
devices with the headline only; tapping it opens the alert in the app.

**Acceptance**
- `OperationalFailure` pushes only to `ops.admin` holders; every other event pushes to its own user.
- Each (event, subscription) is delivered at most once; retries do not duplicate.
- A subscription the push service reports gone is removed.
- Changing Ledger's notification mode has no effect on whether a push is sent.

## Requirements

- **FR-001**: Push MUST be sourced from the Companion outbox and MUST NOT read or write `Disposition` or depend
  on the agent `NotificationMode`.
- **FR-002**: Push MUST be off until the user opts in, and MUST NOT be requested without a user gesture.
- **FR-003**: The notification payload MUST contain only the kind, the subject and a deep link; it MUST NOT
  contain amounts, merchant names, balances or tickers' P&L.
- **FR-004**: Subscriptions and delivery records MUST be owner-scoped (per-user query filter) with isolation
  tests; cross-user jobs opt out explicitly.
- **FR-005**: `OperationalFailure` MUST be delivered only to users holding the `ops.admin` permission.
- **FR-006**: VAPID keys MUST come from configuration/secrets only; absence disables push without failing
  startup. Keys MUST NOT be logged or committed.
- **FR-007**: Delivery MUST be idempotent per (event, subscription) and MUST prune dead subscriptions.
- **FR-008**: The Telegram/agent flow MUST be unchanged by this feature.

## Success Criteria

- **SC-001**: Switching Ledger between Quiet, Digest, Scan and Realtime produces identical push behavior.
- **SC-002**: A Member without `ai.use` receives push for their own alerts; no non-`ops.admin` user ever
  receives `OperationalFailure`.
- **SC-003**: No notification payload carries an amount or merchant (asserted by test).

## Out of Scope

- Changing how Ledger decides to message on Telegram.
- Email/SMS channels.
