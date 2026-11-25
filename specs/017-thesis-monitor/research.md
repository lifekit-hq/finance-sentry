# Phase 0 Research: Thesis Break Monitor

All unknowns from Technical Context resolved below. Sources are the existing codebase
(grounded 2026-07-08) and the spec's approved `[DECISION]` notes.

## R1 — Where the break-state lives

- **Decision**: Reuse `InvestmentThesis.BrokenAt` / `BrokenReason` as the break-state of record.
- **Rationale**: Both fields already exist on the entity and are persisted (jsonb table
  `research.theses`, `BrokenReason` max 1000). No writer sets them today — this feature is
  their first writer. Matches spec `[DECISION]` (reuse, do not create `ThesisCandidate`/`ThesisTrigger`).
- **Alternatives**: A separate `ThesisBreak` table — rejected; adds schema for state the entity
  already models and complicates idempotent clear (FR-011).

## R2 — Trigger shape extension

- **Decision**: Extend the jsonb record
  `ThesisInvalidationTrigger(string Metric, string Direction, decimal Threshold)` →
  add `string? ProxyTicker`, `int ConsecutivePeriods = 1`, `ThesisPeriodType PeriodType = Quarter`.
  Migration **M004** rewrites the `invalidation_triggers` jsonb and backfills the two seeded theses.
- **Rationale**: The collection is already stored as jsonb via `HasConversion` (System.Text.Json,
  Web defaults). Adding nullable/defaulted fields is backward-tolerant on read (missing keys
  deserialize to defaults), but the spec requires an explicit backfill to the exact target state
  (DRAM/GRAB table), so M004 does a data migration, not just a shape note.
- **Alternatives**: A parallel structured trigger table — rejected per spec (no new time-series/
  trigger persistence; stateless derivation).

## R3 — Fundamentals source & window

- **Decision**: `ISecEdgarService.GetFundamentalsAsync(ticker, maxPerConcept, ct)` returning
  `IReadOnlyList<FundamentalFact>`; request `maxPerConcept >= 8` to cover 2-quarter + YoY windows.
- **Rationale**: `FundamentalFact(Ticker, Concept, Label, Unit, Value, PeriodEnd, FiscalPeriod,
  FiscalYear, Form)` carries fiscal period/year, enabling stateless YoY (same fiscal period prior
  year) and consecutive-period selection. Service is a singleton with 12h result cache — fits SC-004.
  Concepts available: Revenue, GrossProfit, OperatingIncome, NetIncome, DilutedEPS, StockholdersEquity.
- **Alternatives**: Persisting a fundamentals time-series — rejected (FR-007: stateless per run).

## R4 — Price-history source (the gap)

- **Decision**: Add `GetDailyClosesAsync(string ticker, DateOnly since, CancellationToken ct)`
  → `IReadOnlyList<DailyClose>(DateOnly Date, decimal Close)` to `IMarketDataService`, implemented
  in `YahooMarketDataService` by retaining the full bar series from the Yahoo
  `/v8/finance/chart/{ticker}?interval=1d` response (currently discarded — only latest/previousClose kept).
- **Rationale**: EDGAR lags price 1–3 months; price triggers (`price_drawdown`, `price_return`) are
  the intraquarter defense (spec 2026-07-07 decision). The existing Yahoo client already fetches
  daily bars; we widen `range` to cover since-creation and keep the closes. `ConsecutivePeriods` for
  price metrics = consecutive quarterly/annual readings per `PeriodType` (#1517), not trading days.
- **Rationale (non-evaluable)**: FR-013/FR-003 — if price history since `CreatedAt` is unavailable,
  the price trigger is non-evaluable, never a break.
- **Alternatives**: A new market-data adapter — rejected; reuse the existing interface + client.
  Note: once feature 018 persists bars, this method can read from storage instead of live Yahoo.

## R5 — Alert emission

- **Decision**: Add `AlertType.ThesisBroken` const to `Modules.Alerts/Domain/AlertType.cs`; add
  `GenerateThesisBreakAlertAsync(...)` and `ResolveThesisBreakAlertAsync(...)` to
  `Core.Interfaces.IAlertGeneratorService`, implemented in `AlertGeneratorService` following the
  `GenerateLowBalanceAlertAsync` pattern: `FindActiveAsync` → `HasRecentAsync` (silence window) →
  `AddAsync`. `ReferenceId` = thesis Id, `ReferenceLabel` = ticker.
- **Rationale**: FR-009 — reuse the module's existing active-alert + silence-window dedup; the
  monitor MUST NOT dedup itself. Consuming via the Core interface keeps Principle I (no Research→Alerts
  module coupling). Severity `Warning`.
- **Alternatives**: Monitor-owned dedup — rejected (FR-009). Direct Alerts module reference — rejected
  (Principle I coupling → automatic block).

## R6 — Idempotency & clearing

- **Decision**: Transition-based. Compute the current verdict per thesis each run; act only on
  edges: `unbroken→broken` sets fields + raises alert; `broken→cleared` clears fields + resolves alert;
  `broken→still-broken` and `unbroken→held` are no-ops.
- **Rationale**: FR-008/FR-011, US3, SC-003 (exactly one alert per transition; no oscillation spam).
  Active-alert check in the generator is the second line of defense.
- **Alternatives**: Manual-reset-only clearing — rejected (Denys confirmed auto-clear 2026-07-06).

## R7 — Scheduling

- **Decision**: `ThesisMonitorJob.ExecuteAsync(CancellationToken)` registered in the **existing**
  `ResearchModule` `JobRegistrar` via `IRecurringJobManager.AddOrUpdate<ThesisMonitorJob>(...)`;
  `services.AddScoped<ThesisMonitorJob>()`. Cron: daily (align with EDGAR/price cadence; `Cron.Daily()`).
- **Rationale**: FR-001; mirrors `NetWorthSnapshotJob`/`AlertPurgeJob`. The job resolves the
  `RunThesisMonitorCommand` handler for all users so schedule and on-demand share one code path.
- **Alternatives**: New Hangfire registrar — rejected; the module already has one.

## R8 — On-demand + read surface (MCP)

- **Decision**: `RunThesisMonitorCommand`/handler (returns run summary) and `ListThesisBreaksQuery`/
  handler; two MCP tools `run_thesis_monitor`, `list_thesis_breaks` in `FinanceSentry.Mcp/Tools/`,
  each `[McpServerToolType]` with primary-ctor DI of the handler + `IIdentityResolver`, resolving
  `userId ?? identity.GetUserId()` (matches `SaveThesisTool`).
- **Rationale**: FR-002/FR-015. Uses hand-rolled `Core.Cqrs` (`ICommand`/`IQuery`), not MediatR.
  Contract coverage: structural `ToolAttributeContractTests` auto-covers; add one parity `[Fact]` per
  new tool in `ToolParityTests`.
- **Alternatives**: REST controller — deferred; MCP + Hangfire is the required surface (no REST
  contract change → no API version bump).

## R9 — Save-path vocabulary guard

- **Decision**: `ThesisTriggerVocabulary` static guard invoked in `SaveThesisCommandHandler`;
  rejects any trigger whose `Metric` is outside the closed 12-key vocabulary (FR-012). Today the
  save path only normalizes ticker/text with no validation, so this is a new inline check (throws a
  domain validation exception).
- **Rationale**: FR-004/FR-012 — reject unsupported metrics at write time; evaluator treats any
  slipped-through unsupported metric as non-evaluable ("unsupported metric").
- **Alternatives**: FluentValidation validator — no validation infra exists in the module; a static
  guard is the minimal consistent addition.

## R10 — Observability run table

- **Decision**: `ThesisMonitorRun` persistence is **deferred to P2** (spec marks it optional). The
  run summary is returned in-band from the command; counts are logged via Serilog. A follow-up
  migration can add the table if auditing demand appears.
- **Rationale**: Spec: "not required for the core loop." Keeps M004 to the trigger reshape only.
