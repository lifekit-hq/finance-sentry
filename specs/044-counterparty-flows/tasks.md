# Tasks: Counterparty Flows (044)

**Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)
**GitHub Issue**: #429

---

## [US1, US2] Slice 1 — Counterparty engine (backend)

### Core layer

- [x] Add `CategoryKeys.FamilySupport = "FAMILY_SUPPORT"` to `CategoryKeys.cs`
- [x] Add `new(CategoryKeys.FamilySupport, "Family Support", 135)` to `CanonicalCategories.Definitions`

### BankSync domain

- [x] Create `BankSync/Domain/Counterparty.cs` entity (UserId, Name, FlowRole, Rules nav)
- [x] Create `BankSync/Domain/CounterpartyRule.cs` entity (CounterpartyId, MatchType, Pattern)
- [x] Add `ICounterpartyRepository` to `IRepositories.cs` (`GetForUserAsync`)

### BankSync infrastructure

- [x] Create `CounterpartyRepository.cs` implementing `ICounterpartyRepository`
- [x] Add `DbSet<Counterparty>` + `DbSet<CounterpartyRule>` to `BankSyncDbContext.cs`
- [x] Configure entity mappings in `BankSyncDbContext.OnModelCreating`
- [x] Create migration `20260902000000_M011_CounterpartyFlows.cs`
  - Creates `counterparties` table
  - Creates `counterparty_rules` table
  - Inserts FAMILY_SUPPORT into `categories`
  - Seeds Оксана Лисенко (rules: "Оксана Лисенко", "мама") with `UserId = Guid.Empty`
  - Seeds Марія Коваль (rules: "Марія Коваль", "Марічка") with `UserId = Guid.Empty`
- [x] Update `BankSyncDbContextModelSnapshot.cs` to include new entities

### BankSync application

- [x] Create `CounterpartyClassificationService.cs` (interface + implementation)
  - `ClassifyAsync`: loads counterparties, matches transactions, nets per month
- [x] Update `MoneyFlowStatisticsService.cs` to inject and apply classification
- [x] Update `MerchantCategoryStatisticsService.cs` to inject and apply classification

### Registration

- [x] Register `ICounterpartyRepository → CounterpartyRepository` in `BankSyncModule.cs`
- [x] Register `ICounterpartyClassificationService → CounterpartyClassificationService` in `BankSyncModule.cs`

### Tests

- [x] Create `Tests.Unit/BankSync/Application/CounterpartyClassificationTests.cs`
  - Match by description_contains
  - Match by merchant_name_contains
  - No match returns empty set
  - Both directions in one month, credits larger
  - Both directions in one month, debits larger
  - Equal credits and debits — neither cancels the other
  - Multi-counterparty in same month
  - Multi-month window produces per-month results
  - First-match wins when multiple rules could match

### Quality gates

- [x] `dotnet build FinanceSentry.sln --no-restore -c Release` → zero warnings
- [x] `dotnet test FinanceSentry.sln --filter "Category!=Integration"` → all pass (553 unit tests, 0 failed)

---

## [US3] Slice 2 — Dashboard split: Spent / Supported family / Kept

- [x] Update `MonthlyFlow` record + `MoneyFlowStatisticsService` to return `FamilySupportOutflowUsd`
- [x] Update dashboard Angular component with the breakdown section
- [x] Update `DashboardStore` computed signals: `monthlySpentFormatted`, `monthlyFamilySupportFormatted`, `monthlyKeptFormatted`
- [x] Playwright test: buckets visible; "supported family" matches backend mock ($800)

---

## [US3] Slice 3 — Invested bucket + one shared classification

### Flow roles

- [x] Create `BankSync/Domain/FlowRoles.cs` (`family_support`, `investment`)
- [x] `MoneyFlowStatisticsService`: only `family_support` net joins Outflow; `investment` net
      is reported as `InvestedOutflowUsd` and stays out of spend
- [x] `MerchantCategoryStatisticsService`: only `family_support` net becomes FAMILY_SUPPORT spend
- [x] Migration `20260903000000_M012_InvestmentRoutingCounterparty.cs` — seed the
      `investment`-role system counterparty (Binance, Interactive Brokers)
- [x] Fix M011: hand-written migrations need inline `[DbContext]` + `[Migration]`, without which
      EF never discovers them

### One classification, passed through (FR-006 / FR-010)

- [x] Add `ICounterpartyClassificationService.ClassifyForWindowAsync(userId, months, ct)`
- [x] `GetMonthlyFlowAsync` / `GetTopCategoriesAsync` take the result as a required parameter
      and no longer inject the classification service
- [x] `DashboardQueryService` classifies once and hands the same result to both readers
- [x] `GetMoneyFlowStatisticsQueryHandler` / `GetTopCategoriesQueryHandler` classify once each

### Frontend

- [x] `MonthlyFlow` model + `MonthTotals`: `investedOutflowUsd`
- [x] `dashboard.computed.ts`: `monthlyInvestedFormatted`, `monthlyKeptFormatted` =
      `inflow − outflow − invested` (clamped), `hasFlowBreakdown`
- [x] `dashboard.component.ts`: fourth "Invested" tile

### Tests

- [x] Backend: investment routing does not inflate outflow; investment credits are not income;
      categories count family support but not investment routing; dashboard classifies once and
      shares the result; flow role rides through matching; window path resolves currencies
- [x] Vitest: four-bucket arithmetic, investing-only month, hidden when empty, no negative Kept
- [x] Playwright: all four tiles visible with the mocked figures (2100 / 800 / 600 / 1300)
- [x] Fix `playwright.config.ts`: html reporter was wiping the json report's `results.json`

### Quality gates

- [x] `dotnet build FinanceSentry.sln -c Release` → zero warnings
- [x] `dotnet test FinanceSentry.sln --filter "Category!=Integration"` → 560 unit tests, 0 failed
- [x] `npm run lint` / `npm run format:check` → clean
- [x] `npm run test:ci` → 189 Vitest tests, 0 failed
- [x] `ng build --configuration=production` + `npx playwright test` → 12 passed, 0 unexpected

---

## [US2] Slice 4 — Directional classification, no netting (this PR)

Owner ruling (2026-09-03): classify per DIRECTION with **no** per-counterparty netting.
Every rent credit is INCOME and every family-support debit is a FAMILY_SUPPORT expense,
even when both involve the same counterparty in the same month.

### Classification engine

- [x] `CounterpartyMonthlyFlow`: `NetIncomeUsd` / `NetExpenseUsd` → `InflowUsd` / `OutflowUsd`
- [x] Drop the `Math.Max(0, credits − debits)` netting — each direction is summed gross
- [x] Order the emitted flows by (month, counterparty name) so FR-009 reproducibility does
      not rest on dictionary enumeration order
- [x] `FlowRoles` docs state what each role means per DIRECTION

### Readers (role-gated, unchanged contract)

- [x] `MoneyFlowStatisticsService`: family_support inbound → Inflow, outbound → Outflow +
      `FamilySupportOutflowUsd`; investment outbound → `InvestedOutflowUsd`, investment
      inbound → neither (capital returning is not income)
- [x] `MerchantCategoryStatisticsService`: FAMILY_SUPPORT is gross outbound, not reduced by
      rent received from the same counterparty

### Tests

- [x] Rewrite the three netting tests as directional ones (credits larger / debits larger /
      equal — none of them cancelling)
- [x] New: re-running over the same batch produces identical ordered flows (FR-009)
- [x] New: money flow — rent in and support out in the same month both land gross
- [x] New: categories — family-support spend is not reduced by rent received

### Docs

- [x] `docs/money-semantics.md` §5.1 — counterparty flows: matching, gross direction rule,
      role table, ordering

### Quality gates

- [x] `dotnet build FinanceSentry.sln -c Release` → 0 errors, 3 pre-existing CS1587 warnings
      in `Modules.Radar` (untouched by this slice)
- [x] `dotnet test FinanceSentry.sln --filter "Category!=Integration"` → 563 unit tests,
      1256 total, 0 failed
