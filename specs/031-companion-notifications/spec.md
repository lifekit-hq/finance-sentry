# Feature Specification: Companion Notification Modes + Event-Driven Push

**Feature Branch**: `031-companion-notifications`
**Created**: 2026-07-22
**Status**: Implemented
**Input**: User description: "Companion notification modes + event-driven push. Finance Sentry owns the policy (materiality, mode preference, dedup/rate-limit/quiet-hours) and dispatches material events to the companion agent; the agent triages and delivers. No new push channels in FS on the agent path (Web Push added later by spec 859)."

## Overview

The companion advisor agent (Ledger) currently reaches Denys only by scheduled polling (a periodic "scan" that runs every couple of hours) plus chat Denys starts himself. Two things are missing, and both are **policy** that belongs in Finance Sentry — the core that owns the financial data, the materiality logic, and Denys's preferences — not in the agent runtime:

1. **Control over how proactive the agent is.** Denys wants to dial the agent between silent, once-a-day, periodic, and immediate — cheaply and reversibly, ideally just by telling the agent.
2. **Real-time reach for things that actually matter.** When a material event happens (a risk rule trips, a thesis breaks, a held name gets a notable street action), Denys wants it to reach him *then* — not on the next poll.

Finance Sentry owns the **policy**: the mode preference, which events are material, dedup/rate-limiting/quiet-hours, and the decision to dispatch. It hands a triage-ready event to the agent runtime, which owns **delivery** (channel, formatting, voice). On the agent path Finance Sentry gains **no** user-facing push channel (a separate Web Push channel is added by [spec 859](../859-web-push-notifications/spec.md)).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Denys controls how proactive the agent is (Priority: P1)

Denys sets a single **notification mode** that governs all *proactive* outreach from the companion agent, on a spectrum from silent to immediate: **quiet** (no proactive outreach at all), **digest** (one consolidated roll-up per day), **scan** (periodic material-event briefs — today's behavior, the default), **realtime** (pushed the moment a material event fires). On-demand chat always works, in every mode. He can change the mode at any time — including by simply telling the agent — and the change takes effect immediately, with no deploy or restart.

**Why this priority**: This is the foundational, independently-valuable slice. Even before any real-time push exists, giving Denys a durable "how much should I hear from you" dial — and moving that preference into Finance Sentry — solves the immediate pain (he muted the agent once for nagging) and is the switch every other behavior reads.

**Independent Test**: Set mode to `quiet`; confirm no proactive message is produced over a window while a direct question still gets answered. Switch to `scan`; confirm periodic briefs resume. Verify the mode persists and that switching required no redeploy.

**Acceptance Scenarios**:

1. **Given** the default mode, **When** Denys queries his current notification mode, **Then** the system reports `scan` (today's behavior is unchanged for existing users).
2. **Given** any mode, **When** Denys sets the mode to a new valid value, **Then** the new mode is persisted and applies to the very next event without any deploy/restart.
3. **Given** `quiet` mode, **When** material events occur, **Then** no proactive outreach is produced, yet the events are still recorded and Denys can still start a chat and get answers.
4. **Given** Denys tells the agent "switch to realtime" in chat, **When** the agent applies it, **Then** the stored mode changes and subsequent material events are pushed immediately.
5. **Given** an invalid mode value, **When** a set is attempted, **Then** it is rejected and the previous mode is unchanged.

---

### User Story 2 - Material events reach Denys in real time (Priority: P2)

When a **material** event occurs — a risk-rule violation, a data sync failure, an unusual-spend detection, a new opportunity, a thesis-invalidation trigger breaking, or a high-conviction street action on a name Denys holds — and the current mode permits proactive outreach, Finance Sentry dispatches a triage-ready "wake" to the companion agent. The agent adds context/judgment and decides whether and how to tell Denys. Finance Sentry never messages Denys directly.

**Why this priority**: This is the reactive capability that makes the agent feel like a companion rather than a poller. It depends on US1 (the mode gates it) but delivers the "tell me when it matters" value.

**Independent Test**: With mode `realtime`, cause a material event (e.g. a risk-rule violation) and confirm a triage-ready dispatch reaches the agent runtime within the target latency, carrying enough context to act, exactly once. With mode `quiet` or `digest`, confirm the same event produces no immediate dispatch but is recorded.

**Acceptance Scenarios**:

1. **Given** `realtime` mode, **When** a material event is detected, **Then** a dispatch reaches the agent within the target latency, carrying the event type, subject, severity, and a reference to pull full detail.
2. **Given** `scan` mode, **When** a material event is detected, **Then** it is NOT pushed immediately but is available for the periodic scan (behavior unchanged from today).
3. **Given** `quiet` mode, **When** a material event is detected, **Then** no dispatch is produced, but the event is recorded with disposition "suppressed by mode."
4. **Given** several correlated detections of the same logical event, **When** they occur close together, **Then** at most one outreach is produced (dedup).
5. **Given** a burst of distinct material events, **When** they exceed the rate limit or fall in quiet-hours, **Then** outreach is throttled/deferred per policy rather than firing every one.
6. **Given** the agent runtime is unreachable when an event fires, **When** dispatch fails, **Then** the event is retried/queued and not silently lost.

---

### User Story 3 - Once-a-day consolidated digest (Priority: P3)

In **digest** mode, material events are withheld from immediate push and rolled up into a single consolidated summary delivered once per day, so Denys gets a calm daily read instead of interruptions.

**Why this priority**: A valuable middle ground between `quiet` and `scan`/`realtime`, but not required for the core control + real-time value. Builds directly on the event recording from US2.

**Independent Test**: Set mode `digest`; over a day, cause several material events; confirm no immediate outreach fires and exactly one consolidated summary covering those events is produced at the daily digest time.

**Acceptance Scenarios**:

1. **Given** `digest` mode, **When** material events occur through the day, **Then** none are pushed immediately.
2. **Given** `digest` mode at the daily digest time, **When** the digest is produced, **Then** it consolidates the day's material events into one summary, and events already reported are not repeated the next day.
3. **Given** `digest` mode and a day with no material events, **When** the digest time arrives, **Then** no empty message is forced (silence is acceptable).

---

### Edge Cases

- **Quiet-hours vs realtime**: a material event fires at 3am in realtime mode → deferred per quiet-hours policy unless flagged critical (e.g. a hard risk breach) which may override.
- **Mode changed mid-flight**: mode switches from realtime to quiet while an event is queued for dispatch → the queued event respects the mode at dispatch time.
- **On-demand always on**: regardless of mode (including quiet), a chat Denys starts is answered — mode never gates responses to him.
- **Non-material events**: routine price wiggles, crypto's normal volatility, or an analyst action on a non-held name → recorded if relevant elsewhere but do NOT count as material for proactive outreach.
- **Duplicate across sources**: the same underlying event surfaced by two detectors (e.g. an alert and a thesis break for the same position) → collapses to one outreach.
- **Agent runtime down during a realtime window**: events accumulate and are delivered/summarized when it returns; none are dropped.
- **First-run / empty state**: right after deploy, event tables may be empty; the system reports honest emptiness, never a fabricated event.

## Requirements *(mandatory)*

### Functional Requirements

**Notification mode (US1)**

- **FR-001**: System MUST persist a per-user notification mode with exactly these values: `quiet`, `digest`, `scan`, `realtime`. Default MUST be `scan` (preserves today's behavior for existing users).
- **FR-002**: System MUST expose reading and setting the mode through the agent tool surface so the agent can report it and change it on Denys's request.
- **FR-003**: A mode change MUST take effect for the next event without any deploy, restart, or code change, and MUST be reversible at any time.
- **FR-004**: The mode MUST govern ONLY proactive outreach. On-demand interaction (a chat Denys initiates) MUST remain fully available in every mode, including `quiet`.
- **FR-005**: Setting an invalid mode MUST be rejected without changing the current mode.

**Materiality + event capture (US2)**

- **FR-006**: System MUST classify events as material per a Finance-Sentry-owned policy. The material event sources are: existing alerts (risk-rule violation, sync failure, unusual spend, opportunity), thesis-invalidation-trigger breaks, and high-conviction street actions on names the user holds.
- **FR-007**: System MUST record every material event and its disposition (`dispatched`, `held-for-digest`, `suppressed-by-mode`, `suppressed-by-dedup`, `suppressed-by-rate-limit`, `deferred-quiet-hours`) for observability. No material event may be silently lost.
- **FR-008**: System MUST deduplicate correlated detections of the same logical event so they yield at most one outreach.

**Dispatch policy (US2/US3)**

- **FR-009**: In `realtime` mode, a material event MUST produce an outbound dispatch to the agent runtime within the target latency (see SC-002), carrying enough context — event type, subject, severity, and a reference to pull full detail — for the agent to triage.
- **FR-010**: In `scan` mode, material events MUST NOT be pushed immediately; they remain available for the existing periodic scan (behavior unchanged from today).
- **FR-011**: In `digest` mode, material events MUST be withheld from immediate push and consolidated into a single daily summary; events already summarized MUST NOT repeat.
- **FR-012**: In `quiet` mode, the system MUST produce no proactive outreach while still recording events.
- **FR-013**: System MUST rate-limit proactive outreach (a cap per rolling window) and honor a quiet-hours window; a small set of critical events MAY override quiet-hours per policy.
- **FR-014**: On dispatch failure (agent runtime unreachable), the system MUST retry/queue the event rather than drop it.

**Boundary**

- **FR-015**: On the agent path, Finance Sentry MUST NOT deliver to any user-facing channel itself (no Telegram/email/SMS in FS). It only records events and dispatches a wake to the agent runtime, which owns delivery, formatting, and channel. *Amended by [spec 859](../859-web-push-notifications/spec.md): Finance Sentry additionally owns a Web Push channel to the user's own installed app, independent of this path.*
- **FR-016**: The dispatch payload MUST NOT embed secrets or full sensitive detail; it carries identifiers/references the agent resolves through existing authenticated tools.

### Key Entities *(include if feature involves data)*

- **Notification Setting**: per-user preference. Attributes: mode (`quiet|digest|scan|realtime`), quiet-hours window, rate-limit parameters, last-updated timestamp. One per user.
- **Material Event**: a captured, policy-classified event eligible for proactive outreach. Attributes: source/type, subject (e.g. ticker or rule key), severity, logical dedup key, detected-at, disposition, dispatched-at. Relationships: many per user; may reference an underlying alert / thesis / analyst action.
- **Dispatch Record**: the outcome of an attempt to wake the agent for an event (or a digest). Attributes: event reference(s), attempt time, status, retry count. Supports the "no event lost" and digest-consolidation guarantees.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Denys can change the notification mode in a single request, and the change is reflected in system behavior on the next event with no redeploy or restart.
- **SC-002**: In `realtime` mode, a material event reaches the agent runtime within **60 seconds** of detection in the normal case.
- **SC-003**: In `quiet` mode over a 24-hour test window with injected material events, the system produces **zero** proactive outreach, while every on-demand question still returns an answer.
- **SC-004**: Correlated duplicate detections of one logical event produce **at most one** outreach.
- **SC-005**: **100%** of material events are recorded with a disposition — none are lost — regardless of mode or agent-runtime availability.
- **SC-006**: In `digest` mode, proactive outreach volume is **at most one message per day**, and it covers that day's material events with no repeats.
- **SC-007**: The agent path adds **no** new outbound user-facing channel; all agent-driven delivery continues to flow through the agent runtime. *Amended by [spec 859](../859-web-push-notifications/spec.md): the Web Push channel is a separate, parallel channel and does not change this criterion for the agent path.*

## Assumptions

- **Reuse existing detectors**: the alerts pipeline (risk, sync failure, unusual spend, opportunity), the thesis-invalidation monitor, and the analyst-actions data already exist and already encode "material enough to alert." This feature consumes those as event sources rather than re-deriving materiality from scratch.
- **"High-conviction street action on a held name"** defaults to: an upgrade/downgrade, a coverage initiation, or a price-target change on a ticker the user currently holds. The exact bar is tunable policy, not a hardcoded constant.
- **Quiet-hours default**: a nightly window in the user's timezone (e.g. ~22:00–07:00); configurable. Only a small explicitly-critical class may override it.
- **Single user in practice**: modeled per-user for correctness, but the deployment serves one primary user (Denys).
- **Agent runtime is the sole delivery path**: the companion agent (in its external runtime) is assumed reachable via an outbound trigger; the exact trigger mechanism is an implementation detail for planning.
- **"Scan" cadence continuity**: the periodic scan that exists today continues to serve `scan` mode; this feature governs *whether* it should proactively surface, not the fine mechanics of the existing periodic job.

## Notes

- **[DECISION] Policy in Finance Sentry, delivery in the agent runtime**: FS owns the mode preference, materiality classification, dedup, rate-limiting, quiet-hours, and the dispatch decision. The agent runtime owns channel, formatting, voice, and the actual send. Rationale: the domain (what matters, and the user's preference) is FS's responsibility as the core; the agent stays a thin consumer. This is the explicit reason the feature lives in FS rather than the OpenClaw agent layer.
- **[DECISION] No new FS push channel on the agent path**: FS triggers the agent (a wake) and never sends to Telegram/email itself, keeping channel ownership in one place on that path. *Amended by [spec 859](../859-web-push-notifications/spec.md): FS additionally owns a separate Web Push channel.*
- **[OUT OF SCOPE] Delivery/formatting/channel-account**: how the agent phrases and where it sends the message lives in the agent runtime, not this feature.
- **[OUT OF SCOPE] Agent-as-code**: moving the agent's persona/jobs/config into this repo and deploying it is a separate feature; this one is only the notification policy + event system.
- **[DEFERRED] Multi-channel fan-out**: if Denys later wants the same policy to also drive an in-app or email notification, the FS-owned mode/materiality is the right seam to extend — deferred to a future feature.
