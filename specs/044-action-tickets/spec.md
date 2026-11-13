# Feature Specification: Action Tickets — Rebalance Order Lists + Cash-Sweep Proposals

**Feature Branch**: `044-action-tickets`

**Created**: 2026-09-02

**Status**: Implementing

**GitHub Issue**: #432

## Context

When IPS allocation bands are breached or idle cash exceeds the configured buffer, the system currently
only flags the condition via radar signals and risk-rule alerts. This feature converts detected drift
into a concrete, confirmable plan: a prioritised order list ("buy X of Equities ≈ $5,230; sell Y of
Cash ≈ $2,100") delivered over the existing Telegram plumbing. Propose-only: nothing executes without
explicit user acknowledgement.

**Sequenced behind #411** (canonical book-figures, closed 2026-08-19) — every figure in a proposal
is sourced from `IBookFiguresService` via the existing `GetAllocationDriftQueryHandler` pipeline.

---

## User Scenarios

### [US1] Rebalance proposal on IPS band breach (P1)

A daily Hangfire job reads `GetAllocationDriftQueryHandler` for each user with an IPS. When
`NeedsRebalance = true`, it computes concrete order lines from out-of-band sleeves (OverBand → sell,
UnderBand → buy, Unplanned → review) and emits a `RebalanceProposal` alert. The alert flows through
the existing Companion → Telegram path.

**Acceptance Scenarios**:

1. **Given** a user's IPS has an Equities sleeve at 60% target and actual is 75%, **When** the job
   runs, **Then** a `RebalanceProposal` alert is created with a sell order for the excess Equities
   notional.
2. **Given** `NeedsRebalance = false`, **When** the job runs for that user, **Then** no alert is
   created or updated.
3. **Given** a proposal was generated for a drift in the last 7 days (open, accepted or dismissed), **When**
   the job runs again against the same drift, **Then** no duplicate alert is created (7-day silence window +
   active-alert dedup). A drift that moved a further band width, or gained or lost a breaching sleeve, supersedes
   the open proposal and notifies again; a drift that cleared and recurs is a new episode (#1266).
4. **Given** a user has no IPS, **When** the job runs, **Then** no alert is created for that user.
5. **Given** the alert fires, **When** the Companion dispatch job runs, **Then** the alert is
   classified as a material `RebalanceProposal` event and routed to Telegram.

### [US2] Cash-sweep proposal on idle cash excess (P2)

When `BookFigures.CashUsd / TotalValueUsd > RiskRuleSet.MinCashBufferPct` by a configurable
tolerance, and the user holds more idle cash than needed, a `CashSweepProposal` alert is generated
suggesting deployment of the excess.

### [US3] One-tap acknowledgement (P2)

A REST endpoint records the user's decision (Accept / Defer) against the open proposal alert.
Companion `AcknowledgeCompanionEventsCommand` marks the event as delivered. No order execution.

### [US4] MCP tool — list open action tickets (P3)

`get_action_tickets` MCP tool lists open (unresolved, unread) `RebalanceProposal` and
`CashSweepProposal` alerts for a user, returning the alert message (order summary) and createdAt.

---

## Functional Requirements

- **FR-001**: The `ActionTicketsGeneratorJob` runs daily at 04:00 UTC — after PortfolioScanner (02:00)
  and Research macro jobs (03:00) — so drift data is always fresh.
- **FR-002**: Every figure in a proposal (notional, total book, drift) MUST be sourced from
  `IBookFiguresService` via `GetAllocationDriftQueryHandler`. No independently derived cash/invested
  numbers anywhere in the proposal path.
- **FR-003**: A proposal that cannot compute order lines from the drift DTO (e.g., no IPS) is NOT sent.
- **FR-004**: Proposal alert is suppressed for 7 days (the pulse checklist's weekly cadence) after the most
  recent one for the same drift signature, using the existing `FindActiveAsync` + silence-window dedup pattern.
  The signature is `{asset class}:{Sell|Buy|Review}:{band widths out}` per breaching sleeve, so a change of one
  IPS band width is the materiality threshold (#1266).
- **FR-005**: `RebalanceProposal` is a new AlertType constant; it maps to a new `CompanionEventKind`
  value and is registered in `MaterialityPolicy.ClassifyAlert`.
- **FR-006**: Order sizing: OverBand sleeve → sell notional = `(ActualValueUsd − TargetPct% × TotalUsd)`;
  UnderBand → buy notional = `(TargetPct% × TotalUsd − ActualValueUsd)`. Unplanned sleeves ≥ 1%
  get a "Review" line.
- **FR-007**: The job lives in `Modules.Research/Infrastructure/Jobs/` (Research already owns IPS +
  drift pipeline; `IAlertGeneratorService` from Core is injected cross-module, following the
  same pattern as `LiquiditySentinelJob` in `Modules.Liquidity`).
- **FR-008**: One-per-user active-alert dedup: the referenceId for portfolio proposals is a stable
  MD5-derived GUID from `userId + "rebalance:portfolio"`, so find/resolve is deterministic.
- **FR-009**: Severity = Warning; Title = "Rebalance proposal: {N} order(s)";
  Message = "Portfolio rebalance required (book ${total:N0}): {order lines}".

---

## Key Entities

- **Alert** (existing): carries the proposal — Type=RebalanceProposal, Title+Message encode the order
  list as a human-readable string. No new DB table in US1.
- **CompanionEvent** (existing): the Companion capture job promotes the alert to a dispatch event,
  routes it through the existing webhook agent-wake path.

## Success Criteria

- **SC-001**: After the job runs for a user with out-of-band sleeves, a `RebalanceProposal` alert
  exists in the alerts table with a non-empty Message listing at least one order line.
- **SC-002**: Running the job twice within 24 hours produces exactly one alert (idempotency via dedup).
- **SC-003**: `MaterialityPolicy.ClassifyAlert("RebalanceProposal")` returns
  `CompanionEventKind.RebalanceProposal`.
- **SC-004**: Unit tests cover: drift-present → order lines built correctly; no IPS → no alert; 
  error in one user → others processed; OverBand/UnderBand notional math.

## Assumptions

- Book-figures data is sourced via `IBookFiguresService` (canonical, #411). No alternative path.
- Research module references `FinanceSentry.Core` (where `IAlertGeneratorService` lives) — confirmed;
  no new project reference needed.
- `IIpsRepository.GetUserIdsWithCurrentIpsAsync` is the user iteration source (same precedent as
  `OpportunityScanJob`).
- Propose-only invariant: no payment initiation or order submission in any story slice.
