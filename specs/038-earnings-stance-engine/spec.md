# Feature Specification: Ledger Earnings-Season Stance Engine

**Feature Branch**: `038-earnings-stance-engine`
**Created**: 2026-08-07
**Status**: Draft
**Input**: Give the Ledger finance agent the Finance Sentry capabilities it needs to autonomously review every holding/watchlist name as it reports earnings, form a bullish/bearish stance, and have that stance logged and scored over time.

## Context & North Star

Ledger (the OpenClaw finance agent) should become an autonomous, opinionated analyst: it forms and voices a bullish/bearish stance on every name the user holds or watches, updates it as facts arrive, and helps the user — with **every call logged and scored so its autonomy earns trust over time**.

This feature delivers only the **Finance Sentry (backend) side**: the facts, the mechanical trigger, the one-call review data, and the logging. The agent's judgment, narrative, and the Telegram scorecard itself are Ledger's job (OpenClaw agent-config) and are **out of scope for this repository**.

Division of labour (established direction — "FS is core, agent is thin"): **FS answers "what / when / how much" and fires triggers; Ledger answers "so what / bull or bear."** Nothing in this feature makes a judgment call or moves capital.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Ledger is woken when a name reports (Priority: P1)

Each day the system checks every name in the user's holdings and watchlist against the earnings calendar. When a name is about to report ("reports today") or has just reported ("reported since the last check"), the system raises a companion event that wakes Ledger to act — routed through the existing companion notification pipeline and gated by the existing materiality policy, so the user is not spammed.

**Why this priority**: Without a trigger, autonomy is impossible — Ledger would only ever review a name if the user manually asked. This is the missing spark that turns "on request" into "the agent just does it during earnings season."

**Independent Test**: Seed a holding whose earnings date is today (or was yesterday); run the daily check; confirm exactly one "reported"/"upcoming" companion event is raised for that user+name+quarter, that it flows to the agent-wake path, and that re-running the check the same day raises no duplicate.

**Acceptance Scenarios**:

1. **Given** a user holds a name reporting today, **When** the daily check runs, **Then** an "earnings upcoming" event is raised for that name and routed to the agent-wake pipeline.
2. **Given** a name reported since the last check, **When** the daily check runs, **Then** an "earnings reported" event is raised once for that name and fiscal quarter.
3. **Given** an event was already raised for a name+quarter, **When** the check runs again that day (or the next), **Then** no duplicate event is raised for the same name+quarter/state.
4. **Given** a name is neither in holdings nor watchlist, **When** it reports, **Then** no event is raised for that user.
5. **Given** the materiality policy would suppress an event under the user's current notification mode, **When** the event is raised, **Then** it is filtered by the existing policy exactly as other companion events are.

---

> **Generalised (thesis-review trigger).** The mechanical trigger this story asks for now exists for every name that has a thesis or an Active candidate, and for more than earnings: `ThesisReviewDue` / `CandidateReviewDue` alerts and companion events are raised once per (subject, source, reference) for a filing, a passed earnings date, a news cluster, a passed `Catalysts` date, a thesis break and an unbreak (`specs/031-companion-notifications/contracts/mcp-tools.md`, "Review kinds"). The earnings source is the post-event half of this story (the date has passed); the "reports today" half is the existing `EarningsAhead` alert. The review bundle (US2), the expectations axis (US3) and scored stances (US4) are unchanged and still open.

### User Story 2 - Ledger reviews a name with a single call (Priority: P1)

When Ledger goes to review a name, it can request one consolidated "earnings review" for that ticker and receive everything needed to judge the **state of the business**: the latest reported-quarter fundamentals, the most recent regulatory filing reference, recent analyst actions and the current recommendation trend, the latest valuation snapshot, and the user's existing thesis for that name (if any). Ledger makes one request instead of orchestrating five.

**Why this priority**: This is the substance of the review. Forcing the agent to hand-assemble five separate lookups per name, for a dozen names, is slow, error-prone, and burns agent budget. One clean bundle is what makes a whole-portfolio sweep practical.

**Independent Test**: Request the review bundle for a ticker that has fundamentals, a filing, analyst coverage, and a thesis on record; confirm the response contains each of those blocks; request it for a bare ticker with none of them and confirm the response returns cleanly with the missing blocks marked absent rather than erroring.

**Acceptance Scenarios**:

1. **Given** a ticker with fundamentals, a recent filing, analyst actions, a valuation snapshot, and a thesis, **When** Ledger requests the review, **Then** all five blocks are returned in one response.
2. **Given** a ticker with no thesis on record, **When** Ledger requests the review, **Then** the response returns successfully with the thesis block marked absent.
3. **Given** a ticker with no coverage at all, **When** Ledger requests the review, **Then** the response returns successfully with each block marked absent (no error).
4. **Given** the review is requested, **When** the response is assembled, **Then** business-trajectory information (fundamentals, filing, valuation) is clearly distinguished from any market-reaction information.

---

### User Story 3 - The review carries an expectations axis (Priority: P2)

The review bundle also carries, as a **separate and clearly labelled second axis**, how the just-reported quarter came in **versus consensus** — actual vs. estimate earnings (and revenue where available), plus the beat/miss direction and magnitude. This is never merged into the business-state view; it is the price/risk lens, kept beside the thesis lens.

**Why this priority**: A great business that guides below consensus still gets repriced — ignoring expectations blinds the agent (and the user) to the market reaction they actually feel. But the business-state scorecard is valuable without it, so this is a fast follow, not a blocker. It also depends on an external data key that may not be present.

**Independent Test**: For a ticker with a recently reported quarter, request the review and confirm the expectations block reports actual vs. estimate and a beat/miss magnitude, kept separate from the business-state blocks; with the data source disabled/keyless, confirm the review still returns and the expectations block is simply marked unavailable.

**Acceptance Scenarios**:

1. **Given** consensus data is available for a reported quarter, **When** Ledger requests the review, **Then** the expectations block shows actual vs. estimate and beat/miss magnitude, separate from business-state.
2. **Given** the consensus data source is unavailable or unconfigured, **When** Ledger requests the review, **Then** the review still returns and the expectations block is marked unavailable (no error, no fabricated numbers).
3. **Given** both axes are present, **When** the review is returned, **Then** they are never collapsed into a single combined rating by the system.

---

### User Story 4 - Every stance is logged and scored (Priority: P1)

After Ledger reviews a name, it records its stance for that name — direction (bullish / bearish / neutral), a conviction level, and a short decision note — as an append-only, price-stamped event. Over time these recorded stances are scored against the market the same way existing thesis lifecycle events are, so the user can see how Ledger's past earnings calls actually aged.

**Why this priority**: This is what makes autonomy trustworthy rather than a firehose of unaccountable opinions. It is a first-class requirement, not polish: a stance that is never recorded can never be graded, and an agent whose calls are never graded can never be shown to be an expert.

**Independent Test**: Record a bullish stance for a ticker; confirm a new append-only, price-stamped event exists carrying the direction, conviction, and note; confirm the existing price-backfill/scoring mechanism picks it up like other tracked events; confirm the record is never mutated after insert (except the standard later price backfill).

**Acceptance Scenarios**:

1. **Given** Ledger has reviewed a name, **When** it records a stance, **Then** an append-only event is stored with direction, conviction, decision note, ticker, timestamp, and price-stamp fields.
2. **Given** a stance event was recorded, **When** the existing track-record scoring runs, **Then** the stance is scored against the benchmark like other tracked events.
3. **Given** a stance event exists, **When** anything other than the standard price backfill occurs, **Then** the recorded direction/conviction/note are never altered.
4. **Given** the user asks how Ledger's earnings calls have performed, **When** the track record is read, **Then** recorded stances appear with their scored outcomes.

### Edge Cases

- A name reports outside market hours or the calendar date is an estimate (unconfirmed) — the "reported" state must only fire on a genuine transition, and estimated dates must not be treated as confirmed reports.
- A name appears in both holdings and watchlist — it must not generate two events for the same report.
- A name is held in a non-US/again-unsupported market with no earnings coverage — it is simply skipped, not errored.
- The earnings calendar source is temporarily unavailable on a given day — the check degrades gracefully and re-attempts next run without emitting false "reported" events or losing a genuine one.
- A ticker symbol is ambiguous or maps to no coverage — the review returns cleanly with empty blocks.
- Ledger records two stances for the same name in one quarter (e.g., a correction) — both are retained (append-only); the latest is the current stance, history is preserved.
- The consensus data key is absent — the expectations axis is silently unavailable everywhere it would appear, consistent with the existing keyless-source behaviour.

## Requirements *(mandatory)*

### Functional Requirements

**Trigger (US1)**

- **FR-001**: The system MUST, on a daily schedule, evaluate every name in each user's current holdings and watchlist against upcoming and recent earnings-report dates.
- **FR-002**: The system MUST raise an "earnings upcoming" signal when a watched name's confirmed report date is the current day, and an "earnings reported" signal when a watched name has transitioned to reported since the previous evaluation.
- **FR-003**: The system MUST route these signals through the existing companion event/notification pipeline and MUST subject them to the existing materiality/notification-mode policy.
- **FR-004**: The system MUST raise each distinct signal at most once per user + name + fiscal quarter + state, persisting enough state to guarantee idempotency across repeated runs.
- **FR-005**: The system MUST only consider a report "confirmed" (not an estimated date) before emitting a "reported" signal, and MUST degrade gracefully (no false or lost signals) when the calendar source is unavailable.

**Review bundle (US2)**

- **FR-006**: The system MUST expose an agent-callable "earnings review" for a single ticker that returns, in one response: latest reported-quarter fundamentals, the most recent relevant regulatory filing reference, recent analyst actions and current recommendation trend, the latest valuation snapshot, and the user's thesis for that name if one exists.
- **FR-007**: The review MUST return successfully when any or all of those blocks are absent, marking each missing block as absent rather than failing.
- **FR-008**: The review MUST clearly separate business-trajectory information from market-reaction information within the response.

**Expectations axis (US3)**

- **FR-009**: The review MUST include a distinct expectations block reporting the just-reported quarter's actual vs. consensus earnings (and revenue where available) and the beat/miss direction and magnitude.
- **FR-010**: The expectations block MUST be sourced from the existing external data integration, MUST be silently unavailable when that source is unconfigured, and MUST never fabricate figures.
- **FR-011**: The system MUST NOT collapse the business-trajectory axis and the expectations axis into a single combined rating.

**Logged stance (US4)**

- **FR-012**: The system MUST let Ledger record an earnings stance for a name as an append-only event carrying direction (bullish/bearish/neutral), conviction, a decision note, ticker, and timestamp.
- **FR-013**: Recorded stance events MUST be price-stamped and scored by the existing track-record mechanism, on the same footing as existing thesis lifecycle events.
- **FR-014**: Recorded stance events MUST be immutable after insert except for the standard later price backfill; the direction/conviction/note MUST never be corrected in place.
- **FR-015**: Recorded stances MUST be readable as part of the existing track record so their scored outcomes can be reviewed.

**Cross-cutting**

- **FR-016**: The feature MUST NOT execute trades or move capital; all output is advisory facts, triggers, and logs.
- **FR-017**: Each piece MUST be independently shippable: the trigger and business-state review MUST function with existing data before the expectations axis is added.

### Key Entities *(include if feature involves data)*

- **Earnings-watch state**: Per user + ticker + fiscal quarter, records which signals have already been emitted, so the daily check is idempotent. The minimal memory that prevents duplicate wakes.
- **Earnings review (composed, not stored)**: A read-time bundle for one ticker aggregating fundamentals, filing reference, analyst actions/recommendation trend, valuation snapshot, thesis, and the expectations axis. Assembled on request from existing data; not a new persisted record.
- **Expectations datum**: Actual vs. consensus earnings/revenue and beat/miss magnitude for a reported quarter, sourced from the existing external integration; may be absent.
- **Earnings stance event**: An append-only, price-stamped record of Ledger's call on a name — direction, conviction, decision note, ticker, timestamp — extending the existing thesis-event track record so it is scored over time.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: During an earnings season, for every name the user holds or watches that reports, Ledger is woken exactly once for that report (no misses, no duplicates) — verifiable by comparing the reported names against the events raised over the season.
- **SC-002**: Ledger can assemble a full per-name review from a single request; a whole-portfolio sweep of a dozen names requires one request per name rather than five.
- **SC-003**: The business-state review is usable and delivers value with no dependency on the external consensus data source (i.e., the scorecard works before the expectations axis exists).
- **SC-004**: When the consensus source is configured, every reviewed just-reported name shows a beat/miss result kept visibly separate from its business-state read.
- **SC-005**: 100% of stances Ledger records are retained immutably and appear in the track record with a scored outcome once prices are available — none are lost or silently overwritten.
- **SC-006**: After one earnings season, the user can answer "how did Ledger's earnings calls do?" directly from the recorded, scored track record.
- **SC-007**: No trade is ever placed and no capital is ever moved by this feature.

## Assumptions

- The existing earnings calendar (holdings + watchlist aware) is the source of report dates; no new calendar source is introduced.
- The existing companion event → dispatch → agent-wake → Telegram pipeline and its materiality/notification-mode policy are reused unchanged; this feature only adds new event kinds and the daily producer.
- The existing research data (fundamentals, filings, analyst actions, recommendation trend, valuation snapshot, thesis) is sufficient for the business-state axis with no new external source.
- The expectations axis reuses the already-integrated external market-data provider and key; when the key is absent the axis is simply unavailable, matching existing keyless-source behaviour.
- The existing thesis-event track record (append-only, price-stamped, benchmark-scored) is the logging and scoring substrate; this feature extends it with an earnings-stance event kind rather than building a parallel mechanism.
- "Holdings" means the user's current brokerage/crypto positions already known to the system; watchlist means the existing research watchlist.
- The agent-side cron, persona rules, and the visual scorecard are delivered as OpenClaw agent-config and are out of scope here.

## Notes

- [DECISION] Reuse over rebuild: the trigger rides the existing companion pipeline (new event kinds only), the review composes existing tools, the expectations axis extends the existing external integration, and stance logging extends the existing thesis-event track record. No new notification path, no new scoring engine, no new calendar source.
- [DECISION] Two axes never merged: business trajectory (thesis lens) and expectations gap (price/risk lens) are always surfaced separately. Rationale: a good business with a bad reaction, and a weak business with a relief rally, are both real, actionable states that a single collapsed rating destroys.
- [DECISION] Logging is first-class, not optional: FR-012–FR-015 ship with the feature, not later. Rationale: autonomy is only trustworthy if past calls are graded; an ungraded agent can never be shown to be an expert.
- [DECISION] Incremental delivery: US1 (trigger) + US2 (business-state review) + US4 (logged stance) form the shippable MVP on existing data; US3 (expectations axis) is a fast follow gated on the external key. Rationale: don't block the whole scorecard on one optional data source.
- [OUT OF SCOPE] Trade execution and capital movement: this feature is advisory only (FR-016). Position sizing remains governed by the (advisory) IPS.
- [OUT OF SCOPE] Ledger's judgment, persona, cron, and the Telegram scorecard rendering: agent-config on OpenClaw, not this repository. FS delivers data + trigger + logging; Ledger decides bull/bear and speaks.
- [OUT OF SCOPE] Any frontend/UI: this is a backend + agent-tool-surface feature only.
