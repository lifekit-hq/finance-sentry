# Feature Specification: Thesis Break Monitor

**Feature Branch**: `017-thesis-monitor`
**Created**: 2026-07-06
**Status**: Implemented
**Input**: The deterministic **monitoring half** of Thesis Radar (`016-thesis-radar`), carved out as a bounded backend service. The discovery/candidate-generation half of the radar is explicitly **deferred** to a separate feature.

## Why this spec exists (design-pass reconciliation)

Ledger drafted `016-thesis-radar` as one large feature with six new entities (`ThesisRadarRun`, `ThesisCandidate`, `ThesisEvidenceItem`, `ThesisTrigger`, `ThesisTheme`, `ThesisSignal`). A design pass split it into two features with very different risk:

- **Monitoring (this spec, `017`)** — deterministic evaluation of *existing* theses against reported fundamentals. Low risk, fully testable, no LLM. Ships now.
- **Discovery (deferred)** — LLM-driven candidate generation ("find underhyped trends"). Fuzzy, noise-prone, human-in-the-loop. Gated behind a quality prototype; **not in scope here.**

The core reconciliation: **the monitoring half needs almost no new schema.** The `InvestmentThesis` entity already carries `InvalidationTriggers`, and already has `BrokenAt` / `BrokenReason` fields. We reuse them. We do **not** create parallel `ThesisCandidate` / `ThesisTrigger` tables.

This service is **tier 1** (deterministic engine) per the finance-sentry architecture: it detects breaks; it does not *interpret* them (that is Ledger / a future reasoning-as-a-service tier) and it does not *deliver* notifications (that is a separate port — it raises a domain Alert only).

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Automatic thesis-break detection (Priority: P1)

A scheduled job evaluates every active thesis's invalidation triggers against the latest reported fundamentals. When a trigger is breached, the thesis is marked broken and a single alert is raised so the user (via any client) can re-evaluate the position.

**Why this priority**: This is the whole feature — automatic, reliable break detection is the value.

**Independent Test**: Seed a thesis with a `gross_margin < 0.35` trigger on a ticker whose reported gross margin is below 0.35 for the last 2 quarters; run the job; assert the thesis is marked broken with a cited reason and exactly one `ThesisBreak` alert exists.

**Acceptance Scenarios**:

1. **Given** an active, unbroken thesis whose trigger condition holds across the required consecutive periods, **When** the monitor runs, **Then** the thesis `BrokenAt`/`BrokenReason` are set and one alert is raised.
2. **Given** an active thesis whose trigger condition does **not** hold, **When** the monitor runs, **Then** the thesis stays unbroken and no alert is raised.
3. **Given** a thesis already marked broken for the same breach, **When** the monitor runs again, **Then** no duplicate alert is raised and `BrokenAt` is unchanged.
4. **Given** a breached trigger references a **proxy ticker** (e.g. DRAM thesis judged by MU), **When** the monitor runs, **Then** it evaluates the proxy ticker's fundamentals, not the thesis ticker's.
5. **Given** a thesis has **no** invalidation triggers, **When** the monitor runs, **Then** it is skipped from break detection and recorded as "no triggers".

### User Story 2 — On-demand evaluation and read via MCP (Priority: P1)

The monitor can be triggered on demand, and current breaks can be read, through the MCP surface — so Ledger and the web UI can query break state and force a re-check without waiting for the schedule.

**Independent Test**: Call `run_thesis_monitor` via MCP, then `list_thesis_breaks`, and verify the second call returns the theses the first marked broken.

**Acceptance Scenarios**:

1. **Given** the user calls `run_thesis_monitor`, **When** it completes, **Then** it returns a run summary (theses evaluated, triggers evaluated, breaks raised, skipped) and persists any break-state changes.
2. **Given** one or more broken theses, **When** the user calls `list_thesis_breaks`, **Then** each is returned with ticker, the breached metric, the observed value(s) and period(s), the threshold, and the human-readable reason.
3. **Given** no broken theses, **When** the user calls `list_thesis_breaks`, **Then** an empty list is returned (not an error).

### User Story 3 — Break state is idempotent and can clear (Priority: P2)

A break is raised once. If the breaching condition later clears in fresh data, the thesis can return to healthy (or stay flagged until the user resets it — see decision). Broken state never oscillates alert spam.

**Acceptance Scenarios**:

1. **Given** a broken thesis whose condition still holds, **When** the monitor runs, **Then** no new alert is raised.
2. **Given** a broken thesis whose condition has cleared in newer reported data, **When** the monitor runs, **Then** the thesis is un-broken (`BrokenAt`/`BrokenReason` cleared) and the associated alert is resolved.
3. **Given** a thesis is un-broken and later breaches again, **When** the monitor runs, **Then** a fresh break and alert are raised.

### User Story 4 — Non-evaluable triggers never false-break (Priority: P2)

When fundamentals are missing (ETF/basket with no EDGAR facts, foreign/OTC name, fewer periods than required, or a denominator of zero), the trigger is skipped and recorded — never treated as a breach.

**Acceptance Scenarios**:

1. **Given** a trigger on a ticker with no EDGAR fundamentals, **When** the monitor runs, **Then** the trigger is skipped and recorded as "not evaluable", and the thesis is not marked broken.
2. **Given** a `revenue_yoy` trigger but only one quarter of data exists, **When** the monitor runs, **Then** the trigger is skipped (insufficient periods) with no break.
3. **Given** a margin trigger where reported Revenue is zero for a period, **When** the monitor runs, **Then** that period is treated as non-evaluable rather than dividing by zero.

### Edge Cases

- Thesis ticker is an ETF/basket → set `proxyTicker` to an EDGAR-filing bellwether; if none, all its triggers are non-evaluable and it is simply never auto-broken.
- Metric string not in the supported vocabulary → the trigger is rejected at save time (see FR-012) and, if somehow present, skipped at evaluation with a recorded "unsupported metric".
- Fundamentals fetch fails for one ticker → that thesis's evaluation is skipped and recorded; the run continues for all other theses.
- Multiple triggers on one thesis → the thesis breaks if **any** trigger breaches (OR semantics); `BrokenReason` names the first breaching trigger with its evidence.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST run a thesis-break monitor on a schedule (recurring Hangfire job) that evaluates all active theses for every user.
- **FR-002**: The system MUST expose an on-demand evaluation entry point through the MCP surface (`run_thesis_monitor`).
- **FR-003**: The system MUST evaluate each thesis's `InvalidationTriggers` against reported fundamentals obtained from the fundamentals provider chain (`IFundamentalsService`, #837: SEC EDGAR us-gaap, then EDGAR ifrs-full, then Yahoo Finance only for a period basis EDGAR cannot supply fresh); price-based metrics evaluate against daily closes from the existing market-data source (`IMarketDataService`), fetched per run — non-evaluable (FR-013) when price history since thesis creation is unavailable.
- **FR-004**: Each invalidation trigger MUST be evaluated deterministically from a **closed metric vocabulary** (see Metric Vocabulary). No free-text parsing and **no LLM** in the evaluation path.
- **FR-005**: A trigger MUST support an optional **proxy ticker** — the ticker whose fundamentals are evaluated (defaults to the thesis ticker when absent). This lets an ETF/basket thesis be judged by a filing bellwether (e.g. DRAM → MU).
- **FR-006**: A trigger MUST support an optional **consecutive-periods** qualifier (default 1). The trigger breaches only when the condition holds for the most recent N reported periods of the trigger's period type.
- **FR-007**: Consecutive-period and year-over-year comparisons MUST be derived **statelessly** from the fundamentals series returned per run. The feature MUST NOT introduce any new persisted financial time-series.
- **FR-008**: On a breach (unbroken → broken transition), the system MUST set `InvestmentThesis.BrokenAt` and `BrokenReason` (citing the metric, observed value(s), period(s), threshold), and MUST raise exactly one alert.
- **FR-009**: The system MUST raise thesis-break alerts through the **existing Alerts module** (`IAlertGeneratorService`, new `AlertType.ThesisBreak`), reusing its existing active-alert/silence-window dedup. The monitor MUST NOT deduplicate alerts itself.
- **FR-010**: The system MUST NOT deliver notifications to any external channel (Telegram, email, etc.). It raises a domain Alert only; delivery is a separate port owned by clients (Ledger, web UI). *(One-way dependency discipline: finance-sentry never pushes to a Ledger-specific channel.)*
- **FR-011**: When a previously broken thesis's condition has cleared in fresh data, the system MUST un-break it (clear `BrokenAt`/`BrokenReason`) and resolve the associated active alert.
- **FR-012**: The thesis save path MUST validate that every invalidation-trigger metric belongs to the supported vocabulary, rejecting unsupported metrics at write time.
- **FR-013**: When fundamentals are missing, insufficient (fewer periods than required), or would divide by zero, the trigger MUST be recorded as non-evaluable and MUST NOT cause a break. A raw-value trigger (`revenue`, `net_income`, `diluted_eps`) whose facts are reported in a currency other than USD (the currency part of the fact's unit, before any `/`) is non-evaluable with reason `currency_mismatch:<code>` (#837; no FX conversion); margins and YoY are unit-free and unaffected.
- **FR-014**: When one thesis or ticker fails evaluation, the run MUST continue for all remaining theses and record the failure in the run summary.
- **FR-015**: The system MUST expose `list_thesis_breaks` through MCP, returning each broken thesis with ticker, breached metric, observed value(s) + period(s), threshold, and reason.
- **FR-016**: The run summary MUST report counts: theses evaluated, triggers evaluated, breaks raised, breaks cleared, triggers skipped, errors.
- **FR-016a** (#837): The run summary MUST also list every thesis with at least one non-evaluable trigger — `UNMONITORABLE` when no trigger was evaluable, `PARTIALLY_MONITORABLE` otherwise — naming each blind trigger's metric, period type, subject ticker, non-evaluable reason and, for a fundamentals metric, the subject's data coverage (which provider supplied which period basis, or why none could). A blind thesis is never reported as intact.
- **FR-017**: The feature MUST NOT execute trades or account actions.

### Metric Vocabulary *(the deterministic core)*

Derived only from the six concepts `get_fundamentals` already returns — **Revenue, GrossProfit, OperatingIncome, NetIncome, DilutedEPS, StockholdersEquity** — computed per reported period:

| Metric key | Definition | Unit |
|---|---|---|
| `gross_margin` | GrossProfit / Revenue | ratio (0–1) |
| `operating_margin` | OperatingIncome / Revenue | ratio |
| `net_margin` | NetIncome / Revenue | ratio |
| `revenue_yoy` | (Revenue[p] − Revenue[same fiscal period, prior year]) / Revenue[prior] | ratio |
| `net_income_yoy` | YoY growth of NetIncome | ratio |
| `operating_income_yoy` | YoY growth of OperatingIncome | ratio |
| `eps_yoy` | YoY growth of DilutedEPS | ratio |
| `revenue` | Revenue (absolute, latest period) | USD |
| `net_income` | NetIncome (absolute, latest period) | USD |
| `diluted_eps` | DilutedEPS (absolute, latest period) | USD/share |

**Price-based metrics** *(added per 2026-07-07 independent review: EDGAR fundamentals lag price by 1–3 months, so a fundamentals-only monitor cannot protect a position intraquarter)* — derived from daily closes of the target ticker (existing market-data source; persisted bars once feature 018 lands):

| Metric key | Definition | Unit |
|---|---|---|
| `price_drawdown` | (peak close since thesis `CreatedAt` − latest close) / peak close | ratio (0–1) |
| `price_return` | (latest close − close at thesis `CreatedAt`) / close at creation | ratio |

Price metrics use `PeriodType` = `Quarter`/`Annual` **not applicable**; they evaluate on the latest close with `ConsecutivePeriods` interpreted as consecutive **trading days** the condition must hold (default 1; recommended ≥ 3 to avoid single-day whipsaw breaks). Example: DRAM `price_drawdown greaterThan 0.30` for 3 days = "break the thesis if the position sits 30% off its peak."

**Direction** values: `lessThan` | `greaterThan` (existing convention). **Breach rule**: for the target ticker (proxy or thesis), compute the metric over the most recent `consecutivePeriods` reported periods of the trigger's period type; the trigger breaches iff `(metric direction threshold)` is true for **all** of those periods.

### Key Entities *(data changes)*

- **InvestmentThesis** *(reuse, no schema change to core)* — `BrokenAt` / `BrokenReason` already exist and become the break-state of record.
- **ThesisInvalidationTrigger** *(extend the existing jsonb record)* — currently `(Metric, Direction, Threshold)`. Add: `ProxyTicker?` (string, nullable), `ConsecutivePeriods` (int, default 1), `PeriodType` (enum `Quarter` | `Annual`, default `Quarter`). Requires a migration for the `theses.invalidation_triggers` jsonb shape plus a **backfill of the two existing theses** (mapping below). `Metric` becomes a constrained vocabulary string.
- **AlertType.ThesisBreak** *(new const)* — added to the existing Alerts `AlertType`, alongside a new `IAlertGeneratorService.GenerateThesisBreakAlertAsync(...)` following the established `GenerateLowBalanceAlertAsync` pattern (active-alert check → silence window → `AddAsync`).
- **ThesisMonitorRun** *(optional, P2 — observability only)* — `RunId, UserId, StartedAt, CompletedAt, ThesesEvaluated, TriggersEvaluated, BreaksRaised, BreaksCleared, Skipped, Errors`. Useful for auditing; **not required** for the core loop and may be a follow-up.

### Existing-thesis backfill (exact target state)

| Thesis | id | Trigger → structured form |
|---|---|---|
| DRAM | `9c091f57-521d-441c-95e1-50400ded1966` | `gross_margin` · proxy `MU` · `lessThan` `0.35` · 2 quarters |
| DRAM | (same) | `revenue_yoy` · proxy `MU` · `lessThan` `0` · 2 quarters |
| GRAB | `e7b9af2c-…` | `revenue_yoy` · proxy `GRAB` (or null) · `lessThan` `0.10` · 2 quarters |
| GRAB | (same) | `operating_margin` · proxy `GRAB` · `lessThan` `0` · 2 quarters |

---

## Success Criteria *(mandatory)*

- **SC-001**: The evaluator is **deterministic** — identical fundamentals + triggers always yield the same verdict — and is covered by unit tests including the consecutive-period, YoY, proxy-ticker, and division-by-zero cases.
- **SC-002**: A trigger with missing or insufficient fundamentals produces **zero** false breaks across the test suite.
- **SC-003**: A break raises **exactly one** alert per unbroken→broken transition; a persisting breach raises no further alerts.
- **SC-004**: A scheduled monitor run completes in under 2 minutes for a user's full thesis set.
- **SC-005**: `list_thesis_breaks` returns, for every broken thesis, at least the breached metric, observed value + period, threshold, and reason — 100% explainable output.
- **SC-006**: The two seeded theses (DRAM, GRAB) evaluate end-to-end against live EDGAR data without error after backfill.
- **SC-007**: The monitor raises only domain Alerts and performs **no** channel delivery (verified by absence of any messaging dependency in the module).

---

## Assumptions & Dependencies

- `IFundamentalsService.GetFundamentalsAsync(ticker, maxPerConcept)` (the #837 provider chain; originally `SecEdgarService.GetFundamentalsAsync`) is the fundamentals source; it returns its coverage plus a per-concept series of `FundamentalFact(Concept, Value, PeriodEnd, FiscalPeriod, FiscalYear, Form, …)` sufficient to derive margins and YoY. Request enough periods (≥8) to cover 2-quarter + YoY windows.
- The Alerts module (`012-alerts-system`) is the emission target; its repository already provides active-alert and recent-alert dedup.
- Hangfire recurring-job scheduling is already established in the codebase (Wealth `NetWorthSnapshotJob`, Alerts `AlertPurgeJob`); follow that pattern.
- Constitution gates apply: `dotnet build` to zero warnings, xUnit coverage on the evaluator, CQRS/MediatR for the on-demand path, MCP tools registered via `WithToolsFromAssembly`.

## Notes / Decisions

- **[DECISION]** Reuse `InvestmentThesis` + `BrokenAt`/`BrokenReason`; do **not** create `ThesisCandidate`/`ThesisTrigger` tables (those belong to the deferred discovery feature).
- **[DECISION]** Evaluation is a **closed metric vocabulary**, not free-text and not LLM — deterministic tier-1 logic only.
- **[DECISION]** Consecutive-period / YoY derive statelessly from the fundamentals series; no new time-series persistence.
- **[DECISION]** Break alerts emit through the existing Alerts module (`ThesisBreak` type); the monitor performs no channel delivery — **delivery is a port** owned by clients.
- **[DECISION]** A trigger's `ProxyTicker` lets a basket/ETF thesis be judged by a filing bellwether (DRAM → MU).
- **[DECISION]** Break state **auto-clears** when the condition resolves in fresh data (FR-011) — confirmed by Denys 2026-07-06. No manual-reset action in v1.
- **[DECISION]** Price-based metrics (`price_drawdown`, `price_return`) added 2026-07-07 per independent practitioner review — fundamentals lag filings by a quarter; price triggers are the intraquarter defense. Existing theses SHOULD get a `price_drawdown` trigger at backfill (threshold per Denys, suggested 0.30).
- **[OUT OF SCOPE]** Candidate/trend discovery, themes, signals, evidence store (deferred discovery feature).
- **[OUT OF SCOPE]** Notification delivery (Telegram/email/web push) and any LLM interpretation of a break.
- **[OUT OF SCOPE]** Backtesting / historical simulation.
- **[MCP CONTRACT]** New tools: `run_thesis_monitor`, `list_thesis_breaks`. Update the MCP tool-count contract test accordingly.
