# Tasks: Liquidity Brain (041)

**Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)
**GitHub Issue**: #430

## [US1, US2, US3] Slice 1 — Projection engine + shortfall sentinel

### Core layer

- [x] Add `IActiveSubscriptionsReader` interface and `ActiveSubscriptionSummary` record to `FinanceSentry.Core/Interfaces/IActiveSubscriptionsReader.cs`
- [x] Add `GetAllActiveUserIdsAsync` and `GetActiveAccountSnapshotsAsync` to `IBankingAccountsReader` + `AccountBalanceSnapshot` record

### Alerts module

- [x] Add `CashShortfall = "CashShortfall"` to `AlertType.cs`
- [x] Add `GenerateCashShortfallAlertAsync` + `ResolveCashShortfallAlertAsync` to `IAlertGeneratorService`
- [x] Implement both methods in `AlertGeneratorService` (24-hour silence window)

### BankSync module

- [x] Implement `GetAllActiveUserIdsAsync` + `GetActiveAccountSnapshotsAsync` in `BankingAccountsReader`

### Subscriptions module

- [x] Implement `IActiveSubscriptionsReader` in `ActiveSubscriptionsReader` (thin repository query, `Status == "active"` AND `Kind == "subscription"`)
- [x] Register `IActiveSubscriptionsReader` → `ActiveSubscriptionsReader` in `SubscriptionsModule`

### Liquidity module

- [x] Create `FinanceSentry.Modules.Liquidity.csproj`
- [x] Implement `CashFlowProjectionService` (pure arithmetic, no DI)
- [x] Implement `LiquiditySentinelJob` (daily Hangfire, no LLM calls)
- [x] Wire `LiquidityModule` (DI registration + IJobRegistrar)
- [x] Add module to `FinanceSentry.sln`
- [x] Add project reference to `FinanceSentry.API.csproj`

### Tests

- [x] Unit tests for `CashFlowProjectionService` (9 cases: shortfall, no shortfall, multi-outflow, same-day multi, out-of-window, cross-currency, annual, zero-balance, shortfall-amount-positive)
- [x] Unit tests for `LiquiditySentinelJob` (6 cases: no-users, null-balance, shortfall fires alert, no-shortfall resolves, multi-account, error-per-user continues)
- [x] Unit tests for `AlertGeneratorService.GenerateCashShortfallAlertAsync` (4 cases: creates, dedup active, silence window, resolve)
- [x] Update `FakeAlertGeneratorService` in Research.Tests to implement new methods

### Quality gates

- [x] `dotnet build FinanceSentry.sln --no-restore -c Release` → zero warnings
- [x] `dotnet test FinanceSentry.sln --filter "Category!=Integration"` → all pass (1167 tests, 6 skipped)
