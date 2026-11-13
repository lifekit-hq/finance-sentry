# Phase 0 Research: Risk Rules

## R1 — Where does turnover/quantity-history come from?

**Decision**: The Risk module persists its own `HoldingSnapshot` table (symbol, quantity, usdValue, capturedAt), written once per `RiskCheckJob` run.

**Rationale**: `BrokerageHolding`/`CryptoHolding` (BrokerageSync/CryptoSync modules) are mutable, upsert-in-place rows (`BrokerageHolding.Update(quantity, usdValue)` overwrites `Quantity`/`SyncedAt` on every sync — see `backend/src/FinanceSentry.Modules.BrokerageSync/Domain/BrokerageHolding.cs`). No module keeps point-in-time history of quantity. Both `FR-001b` (turnover budget — count discretionary trades/quarter) and `FR-006`/US3 (flag a quantity increase on a broken thesis, and specifically *not* flag an increase that predates the break) require comparing a **snapshot at time T against a snapshot at time T-1**. Rather than retrofitting history into BrokerageSync/CryptoSync (cross-module schema change, against Principle I), Risk keeps its own append-only capture of what it read on each check run — a read-side cache, not a system of record. This mirrors the existing precedent of `NetWorthSnapshot` (Wealth module) doing exactly this pattern for balances.

**Alternatives considered**: (a) Add a `HoldingHistory` table to BrokerageSync/CryptoSync — rejected, couples Risk's needs into unrelated modules' schemas. (b) Compute turnover from BankSync/BrokerageSync transaction logs — rejected for v1: brokerage "trades" aren't uniformly logged as transactions across all three sync modules yet; quantity-snapshot diffing is simpler and sufeixes for the FR as written (a rolling quarterly count, not a precise trade ledger).

## R2 — CQRS pattern: hand-rolled `Core.Cqrs`, not MediatR

**Decision**: All new commands/queries implement `FinanceSentry.Core.Cqrs.IQuery<T>`/`IQueryHandler<TQuery,T>` and `ICommand<T>`/`ICommandHandler<TCommand,T>` (see `backend/src/FinanceSentry.Core/Cqrs/Queries.cs`, `Commands.cs`), matching the pattern already used in the Research module (`GetAllocationDriftQuery` et al.). Older modules (Alerts, Wealth per their plan.md) reference MediatR in their historical plan docs, but the current `Core.Cqrs` folder is the live convention for any module built after Research — this plan follows the current, not the historical, convention per the task brief.

## R3 — Cross-module reads: precedent is established, not new

**Decision**: `RiskEvaluationService`'s book aggregation follows `GetAllocationDriftQueryHandler` exactly — inject `ICryptoHoldingsReader`, `IBrokerageHoldingsReader`, `IBankingAccountsReader` (all `FinanceSentry.Core.Interfaces`), wrap each source in try/catch, and treat a failed source as "stale, not zero" rather than silently excluding it. `IThesisRepository` (Research module) is read directly for `FR-006`, matching how Research's own handler reads `IIpsRepository` — reading another module's repository interface directly (not through a Core abstraction) is the existing pattern for same-boundary "wealth-adjacent" modules; Risk follows suit rather than inventing a new indirection layer for a single boolean (`IsBroken`) lookup.

## R4 — Alerts integration: extend `IAlertGeneratorService`, don't bypass it

**Decision**: Add `GeneratePolicyViolationAlertAsync(userId, ruleKey, subject, observedValue, limitValue, severity, ct)` and `ResolvePolicyViolationAlertAsync(userId, ruleKey, subject, ct)` to the existing `IAlertGeneratorService` interface (Core), implemented in `AlertsModule`'s `AlertGeneratorService`, alongside a new `AlertType.PolicyViolation` const. This is the same shape as the existing `GenerateLowBalanceAlertAsync`/`GenerateSyncFailureAlertAsync` methods — Risk never references `FinanceSentry.Modules.Alerts` directly, only the Core interface, preserving Principle I's dependency-inversion rule.

## R5 — MCP tool surface: 3 new tools, contract test must move in lockstep

**Decision**: `check_risk_rules`, `get_risk_rules`, `save_risk_rules` follow the exact shape of `GetAllocationVsTargetTool`/`GetIpsTool`/`SaveThesisTool` (`[McpServerToolType]`, `[McpServerTool(Name = "...")]`, inject `IIdentityResolver` + the relevant `IQueryHandler`/`ICommandHandler`, default `userId` to the resolved identity). `ToolNameContractTests.AgreedToolSurface` (currently 27 tools) is a hash-set equality assertion — the PR that ships the tools MUST update that set in the same commit or the contract test fails by design (that's its job).

## R6 — No composite score, no LLM — how "facts only" cashes out in code

**Decision**: `RiskEvaluationService.Evaluate(...)` returns a list of `PolicyViolation` records (RuleKey, Subject, Observed, Limit, ExcessUsd/ExcessPct, Status) — never a blended 0–100 "risk score." `RiskVerdict` for the promotion-time check (`FR-004`) is a plain enum `Allowed | Refused` plus the specific rule that would be violated and the max compliant size — again, no numeric score. This mirrors the ROADMAP's explicit rejection of "false precision" in 019's opportunity scorecard, applied here to risk.

## R7 — Correlation/stress facts (FR-001d) — optional, degrades cleanly pre-018

**Decision**: `RiskEvaluationService` accepts an optional `IPriceBarsReader`-shaped dependency (018's future interface) behind a nullable service resolution — if 018 hasn't shipped, `CorrelationFacts`/`StressLine` fields on the compliance report are simply `null`, and nothing in the FR-001–FR-008 core logic depends on them. This keeps 022 shippable independent of 018's sequencing (per ROADMAP: 018 ships third, 022 fourth, but neither hard-blocks the other's code).

## R8 — Acknowledgement + worsening-step semantics (FR-003, SC-002)

**Decision**: `PolicyViolationAck` stores `(RuleKey, Subject, AcknowledgedAt, RemediationNote, WorseningStepPct)`. On each check run, if a matching violation exists and its excess (`observed - limit`) has grown by more than `WorseningStepPct` since acknowledgement, the violation's `Status` flips from `Acknowledged` back to `New` and a fresh alert fires; otherwise it reports `Acknowledged` silently (`SC-002`: the 46%-vs-25% DRAM case produces exactly one alert, then survives re-runs once acknowledged). Default `WorseningStepPct` is a configurable field on the ack (not hardcoded), consistent with "the system never invents limits" (spec Assumptions).

> **Status (2026-11-13, #558 stage 2)**: the pairwise facts are computed by `IMarketStructureReader.GetPairwiseCorrelationsAsync` and read by `CheckRiskRulesQuery`. The same 63-day return maths now also backs `IReturnCorrelationReader` (a ticker against the top five holdings and the book; Radar `ReturnCorrelation`), which feeds the `score_candidate` correlation fact (019 contracts) and the D1 add-rule penalty.
