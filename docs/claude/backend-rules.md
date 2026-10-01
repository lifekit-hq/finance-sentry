# Backend Rules (mandatory gates)

## Backend Build Gate — mandatory

After writing or modifying **any** `.cs` file, run `dotnet build backend/` and fix **all warnings** before moving on. Non-negotiable:
- Remove unused `using` directives (`IDE0005`)
- Apply primary constructor where suggested (`IDE0290`)
- Resolve nullable reference warnings (`CS8618`, `CS8600`–`CS8604`) — do not suppress with `!` without a comment
- Apply safe IDE suggestions (`IDE0161`, `IDE0028`, `IDE0059`) that do not change runtime behaviour
- Zero warnings before the task is marked complete — same standard as the ESLint gate

Use `/csharp-quality` for a batch cleanup sweep across multiple files.

---

## Currency / Money Aggregation Rule — mandatory

Accounts span currencies (Monobank=UAH, Revolut/AIB=EUR, IBKR/Binance=USD, …). **Never sum a native `Amount`/`Balance` across accounts** — a raw `.Sum(x => x.Amount)` adds hryvnia to euros as if both were dollars (this caused the "$28k monthly outflow" and "Government $71k top spending" bugs).

The convention, enforced structurally:
- **Convert once at the reader/query boundary**, where the account currency is in scope, via `FinanceSentry.Core.Utils.CurrencyConverter.ToUsd(amount, currency)` (the single conversion primitive; the FX refresh job feeds its rate table).
- **Every DTO that crosses an aggregation boundary carries a `…Usd` / `…InBaseCurrency` field** (e.g. `BankingTransactionSummary.AmountUsd`, `BankingAccountSummary.BalanceUsd`, `CryptoHoldingSummary.UsdValue`). Aggregations sum **only** that field, never the native one.
- When you add a new totals/summary path over transaction-level data, thread the account currency in and expose a converted field — do not sum native amounts and "fix it later".
- Unknown currency: `CurrencyConverter.ToUsd` falls back to 1:1. Use `CurrencyConverter.IsKnown(currency)` if you need to flag a total as approximate rather than trust a silent fallback.

---

## Owner Query Filter - per-user DbContexts

A DbContext converted to owner scoping takes `ICurrentUser` (`FinanceSentry.Core.Auth`) and declares the named filter on every per-user entity: `HasQueryFilter(OwnerQueryFilter.Name, e => e.UserId == CurrentUserId)`. A request sees only its principal's rows; a background job has no principal, so the filter matches nothing.

- Every job or sweep that legitimately reads across users opts out explicitly with `IgnoreQueryFilters([OwnerQueryFilter.Name])` and keeps its own `UserId` predicate. Inserts and `SaveChanges` are not filtered; `ExecuteUpdate`/`ExecuteDelete` are.
- Each converted context ships a two-user isolation test and a no-principal test per cross-user job (see `AlertsOwnerQueryFilterTests`).
- Hosts register `ICurrentUser` explicitly (API: `HttpContextCurrentUser`; MCP: `IdentityResolverCurrentUser`); design-time factories pass `NoCurrentUser.Instance`.
- Repository reads that jobs, anonymous callbacks or cross-module readers need are separate `…Unscoped…` methods that opt out with `IgnoreQueryFilters([OwnerQueryFilter.Name])` and keep their own `UserId` predicate; the unsuffixed methods stay filtered for the request path. A no-principal caller must never use an unsuffixed read.
- An entity with a shared corpus (`ResearchDocument`: `UserId == null` rows are visible to everyone) declares `e.UserId == null || e.UserId == CurrentUserId`; owned rows stay owner-only.
- Converted so far: `AlertsDbContext`, `BankSyncDbContext`, `BrokerageSyncDbContext`, `CryptoSyncDbContext`, `ResearchDbContext`. Sync upserts and dedup reads must opt out too: with no principal a filtered existence check finds nothing and re-inserts duplicates.
