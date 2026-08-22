# Feature Specification: Liquidity Brain

**Feature Branch**: `041-liquidity-brain`
**GitHub Issue**: #430
**Created**: 2026-08-22
**Status**: In Progress
**Input**: Project every account 30 days forward from its recurring flows and warn BEFORE an account runs dry, rather than after. Recurrence detection reuses the subscriptions module's prior art. The daily re-check is deterministic and makes no LLM call unless a threshold trips. Shortfall alert rides the existing Alerts → Companion → Telegram pipeline.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — 30-Day Per-Account Cash-Flow Projection (Priority: P1)

The system projects each account's balance 30 days into the future by subtracting recurring outflows on their expected dates. If the projected balance drops below zero on any day, the system fires a "CashShortfall" alert naming the account, the projected date of first shortfall, and the amount needed.

**Why this priority**: Without the projection, there is no shortfall detector. This is the foundation.

**Independent Test**: Given an account with €200 balance and a detected €250 subscription due in 10 days, the projection shows a −€50 shortfall on day 10.

**Acceptance Scenarios**:

1. **Given** an account has a non-zero current balance and at least one active recurring outflow (from DetectedSubscription), **When** the daily sentinel job runs, **Then** the system produces a 30-day balance projection for that account.
2. **Given** the projection shows the balance going negative before day 30, **When** the job runs, **Then** a `CashShortfall` alert is raised naming the account, the projected shortfall date, and the shortfall amount.
3. **Given** the balance never goes negative within 30 days, **When** the job runs, **Then** no shortfall alert is raised (and any existing one is resolved).
4. **Given** an account has no active subscriptions, **When** the job runs, **Then** the balance is projected as flat (no outflows) and no alert is raised.
5. **Given** a shortfall alert was raised previously but the balance is now sufficient, **When** the job runs, **Then** the existing alert is resolved.

---

### User Story 2 — Daily Deterministic Re-Check (Priority: P1)

The liquidity sentinel runs as a nightly Hangfire job, makes no LLM call, and completes in deterministic time proportional to the number of accounts × subscriptions.

**Why this priority**: The system must be reliable and cheap. No LLM in the hot path.

**Acceptance Scenarios**:

1. **Given** the sentinel job is registered, **When** the scheduler fires, **Then** it runs once per day without manual intervention.
2. **Given** any execution of the sentinel job, **Then** it makes no call to any LLM API — all projection logic is pure arithmetic.
3. **Given** a user has no active bank accounts or no connected providers, **When** the job runs, **Then** it skips gracefully without error.

---

### User Story 3 — Shortfall Alert Riding the Existing Pipeline (Priority: P1)

The `CashShortfall` alert type is a first-class alert type that flows through the existing Alerts → Companion → Telegram path, with a 24-hour silence window to prevent daily spam.

**Acceptance Scenarios**:

1. **Given** a shortfall is detected, **When** the alert is created, **Then** it carries: type=`CashShortfall`, severity=`Warning`, title naming the account, message with the projected date and shortfall amount.
2. **Given** a shortfall alert exists and the same shortfall persists the next day, **When** the job runs again, **Then** no duplicate alert is fired (silence window / active-alert dedup).
3. **Given** a shortfall alert exists but the condition resolves (balance topped up or subscription cancelled), **When** the job runs, **Then** the alert is resolved.

---

### Edge Cases

- Account with null/unknown balance: skip projection for that account (can't project from unknown start).
- Subscription currency differs from account currency: for v1, only same-currency subscriptions are applied to an account; cross-currency subscriptions are excluded.
- Multiple outflows on the same day: all subtracted on that day.
- Annual subscriptions: next expected date is included if it falls within 30 days.
- Account with zero balance and no outflows: no alert (balance stays at 0, never negative).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST project each active bank account's balance 30 days forward by subtracting active subscriptions whose `NextExpectedDate` falls within the projection window.
- **FR-002**: The system MUST raise a `CashShortfall` alert when the projected balance first goes negative, including the account name, projected shortfall date, and shortfall amount.
- **FR-003**: The system MUST resolve an existing `CashShortfall` alert when the projected balance no longer goes negative within 30 days.
- **FR-004**: The sentinel job MUST run daily via Hangfire with zero LLM calls.
- **FR-005**: Accounts with null `CurrentBalance` MUST be skipped.
- **FR-006**: Only subscriptions in the same currency as the account are applied in v1 (cross-currency excluded).
- **FR-007**: The `CashShortfall` alert MUST carry the account name, projected shortfall date (ISO date), and shortfall amount as part of the message.
- **FR-008**: The shortfall sentinel MUST use a 24-hour silence window to prevent daily duplicate alerts (active-alert dedup prevents re-creation when an active alert already exists).

### Key Entities *(include if feature involves data)*

- **No new persistent entities**: the projection is computed at runtime from `BankAccount.CurrentBalance` + active `DetectedSubscription` records.
- New read-only cross-module interface `IActiveSubscriptionsReader` (in Core.Interfaces).
- New alert type constant `CashShortfall` (in Alerts module).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A unit test with a seeded account + subscription shows the correct shortfall date and amount.
- **SC-002**: A unit test with sufficient balance shows no shortfall detected.
- **SC-003**: The Hangfire job is registered and its registration can be verified.
- **SC-004**: `dotnet test --filter Category!=Integration` passes with zero failures.
- **SC-005**: `dotnet build` produces zero warnings.

## Assumptions

- Recurring inflows (salary etc.) are out of scope for this slice; the projection is conservative (only outflows subtracted from current balance).
- "Active" subscriptions = `Status == "active"` and `Kind == "subscription"` in DetectedSubscription.
- The sentinel processes all users with at least one active bank account — it is not user-opted-in.
- The existing Alerts → Telegram delivery path already handles the new alert type without changes.
- Cross-currency outflow conversion is deferred; v1 only uses subscriptions whose currency matches the account currency.

## Notes

- [DECISION] New module `FinanceSentry.Modules.Liquidity` — isolates projection logic; follows the modular monolith pattern.
- [DECISION] `IActiveSubscriptionsReader` interface in Core.Interfaces — same cross-module pattern as `IBankingAccountsReader`.
- [DECISION] Silence window: active-alert dedup (no duplicate while alert is open) + 24h HasRecent check after resolution, matching `LowBalance` pattern.
- [OUT OF SCOPE] Recurring inflow detection (salary, rent income) — slice 2.
- [OUT OF SCOPE] Idle-cash sweep advisor — stretch branch per the issue.
- [OUT OF SCOPE] User-configurable shortfall threshold.
