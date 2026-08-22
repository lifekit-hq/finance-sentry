# Tasks: Liquidity Brain (041)

**Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)
**GitHub Issue**: #430

## [US1, US2, US3] Slice 1 — Projection engine + shortfall sentinel

### Core layer

- [x] Add `IActiveSubscriptionsReader` interface and `ActiveSubscriptionSummary` record to `FinanceSentry.Core/Interfaces/IActiveSubscriptionsReader.cs`

### Alerts module

- [x] Add `CashShortfall = "CashShortfall"` to `AlertType.cs`
- [x] Add `GenerateCashShortfallAlertAsync` + `ResolveCashShortfallAlertAsync` to `IAlertGeneratorService`
- [x] Implement both methods in `AlertGeneratorService` (24-hour silence window)

### Subscriptions module

- [x] Implement `IActiveSubscriptionsReader` in `FinanceSentry.Modules.Subscriptions` (thin repository query, `Status == "active"` AND `Kind == "subscription"`)

### Liquidity module

- [x] Create `FinanceSentry.Modules.Liquidity.csproj`
- [x] Implement `CashFlowProjectionService` (pure arithmetic, no DI)
- [x] Implement `LiquiditySentinelJob` (daily Hangfire, no LLM calls)
- [x] Wire `LiquidityModule` (DI registration + IJobRegistrar)
- [x] Register `LiquidityModule` in `FinanceSentry.API/Program.cs`

### Tests

- [x] Unit tests for `CashFlowProjectionService` (shortfall detected, no shortfall, zero balance, multiple outflows, no outflows)
- [x] Unit tests for `LiquiditySentinelJob` (shortfall triggers alert, no shortfall resolves alert, null balance skipped)

### Quality gates

- [x] `dotnet build FinanceSentry.sln --no-restore` → zero warnings
- [x] `dotnet test FinanceSentry.sln --filter "Category!=Integration"` → all pass
