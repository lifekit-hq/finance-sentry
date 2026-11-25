# Phase 1 Data Model: Thesis Break Monitor

Schema changes are **minimal**: one jsonb reshape + data backfill (migration M004). No new
tables in v1. All types live in `FinanceSentry.Modules.Research`.

## Modified: `ThesisInvalidationTrigger` (jsonb value record)

Stored inside `research.theses.invalidation_triggers` (jsonb list, System.Text.Json Web defaults).

| Field | Type | Rule | Notes |
|---|---|---|---|
| `Metric` | string | MUST ∈ Metric Vocabulary (FR-012) | constrained at save (R9) |
| `Direction` | string | `lessThan` \| `greaterThan` | existing convention |
| `Threshold` | decimal | — | compared per breach rule |
| `ProxyTicker` | string? | nullable; defaults to thesis ticker when absent (FR-005) | e.g. DRAM→`MU` |
| `ConsecutivePeriods` | int | default `1`; ≥1 | fundamentals: reported periods; price: quarterly/annual readings per `PeriodType` (#1517) |
| `PeriodType` | `ThesisPeriodType` enum | `Quarter` (default) \| `Annual` | price metrics: one reading per calendar quarter/year (#1517) |

**Backward read**: missing `ProxyTicker`/`ConsecutivePeriods`/`PeriodType` keys deserialize to
defaults; M004 still performs the explicit backfill below so persisted rows match the target state.

## New: `ThesisPeriodType` enum

`Quarter` | `Annual`. For price metrics (`price_drawdown`, `price_return`) `PeriodType` selects the
sampling: each calendar quarter or year contributes one reading (its last close so far, so the
in-progress period counts), and `ConsecutivePeriods` counts those readings, not trading days (#1517).

## Reused (no change): `InvestmentThesis`

| Field | Role in this feature |
|---|---|
| `Id` (Guid) | alert `ReferenceId` |
| `UserId` (Guid) | user-scoping; run iterates active theses per user |
| `Ticker` (string) | default evaluation target; alert `ReferenceLabel` |
| `CreatedAt` (DateTimeOffset) | anchor for `price_return` (close at creation) and drawdown peak window |
| `InvalidationTriggers` (List) | the triggers evaluated (OR semantics — break if any breaches) |
| `BrokenAt` (DateTimeOffset?) | set on break, cleared on resolve (state of record) |
| `BrokenReason` (string?, max 1000) | cites metric, observed value(s), period(s), threshold |

**State transitions** (per thesis, per run):

```
unbroken --(any trigger breaches)-->  broken     : set BrokenAt/BrokenReason, GenerateThesisBreakAlert
broken   --(condition holds)------->  broken     : no-op (no new alert)
broken   --(condition cleared)----->  unbroken   : clear BrokenAt/BrokenReason, ResolveThesisBreakAlert
unbroken --(no breach / non-eval)-->  unbroken   : no-op
```

## New in-memory types (not persisted)

### `DailyClose` (market-data DTO)
`(DateOnly Date, decimal Close)` — returned by `IMarketDataService.GetDailyClosesAsync`.

### `TriggerVerdict` (evaluator output)
Discriminated result:
- `Breached(string Metric, decimal[] ObservedValues, string[] Periods, decimal Threshold, string Direction)`
- `Held`
- `NonEvaluable(string Reason)` — reasons: `no_fundamentals`, `insufficient_periods`,
  `divide_by_zero`, `no_price_history`, `unsupported_metric`.

### `ThesisEvaluation` (per-thesis outcome)
`(Guid ThesisId, string Ticker, TriggerVerdict FirstBreach?, IReadOnlyList<TriggerVerdict> All, bool ShouldBreak, bool ShouldClear)`.
`BrokenReason` is composed from `FirstBreach` (the first breaching trigger, OR semantics — edge case).

### `ThesisMonitorRunSummary` (command result / MCP output)
`(int ThesesEvaluated, int TriggersEvaluated, int BreaksRaised, int BreaksCleared, int Skipped, int Errors)` (FR-016).

### `ThesisBreakView` (list_thesis_breaks item)
`(Guid ThesisId, string Ticker, string Metric, decimal[] ObservedValues, string[] Periods, decimal Threshold, string Direction, string Reason)` (FR-015, SC-005 — 100% explainable).

## Metric Vocabulary (closed set — the deterministic core)

Fundamentals-derived (per reported period, from the six EDGAR concepts):
`gross_margin`, `operating_margin`, `net_margin`, `revenue_yoy`, `net_income_yoy`,
`operating_income_yoy`, `eps_yoy`, `revenue`, `net_income`, `diluted_eps`.

Price-derived (daily closes since thesis `CreatedAt`):
`price_drawdown`, `price_return`.

Definitions per spec §Metric Vocabulary. Any metric outside this set → rejected at save (FR-012),
`NonEvaluable("unsupported_metric")` if encountered at eval.

## Migration M004_ThesisTriggerV2 — backfill target state

| Thesis | id | Triggers after backfill |
|---|---|---|
| DRAM | `9c091f57-521d-441c-95e1-50400ded1966` | `gross_margin` proxy `MU` `lessThan` `0.35` · 2Q; `revenue_yoy` proxy `MU` `lessThan` `0` · 2Q; `price_drawdown` `greaterThan` `0.30` · 3Q *(suggested, per 2026-07-07 decision)* |
| GRAB | `e7b9af2c-…` | `revenue_yoy` proxy `GRAB`(or null) `lessThan` `0.10` · 2Q; `operating_margin` proxy `GRAB` `lessThan` `0` · 2Q |

> Confirm the exact GRAB id and whether DRAM's `price_drawdown` threshold is 0.30 before running
> the data migration (open item carried into tasks.md).
