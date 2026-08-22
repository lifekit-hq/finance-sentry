# Implementation Plan: Liquidity Brain (041)

**Spec**: [spec.md](spec.md)
**GitHub Issue**: #430
**Created**: 2026-08-22

## Architecture

### New Module: `FinanceSentry.Modules.Liquidity`

Follows the same modular monolith pattern as every other module. No persistent entities — the projection is computed at runtime.

```
backend/src/FinanceSentry.Modules.Liquidity/
  Application/
    Services/
      CashFlowProjectionService.cs   # Pure projection arithmetic
    Jobs/
      LiquiditySentinelJob.cs        # Daily Hangfire job
  LiquidityModule.cs                 # DI registration + IJobRegistrar
  FinanceSentry.Modules.Liquidity.csproj
```

### Cross-Module Interface (Core.Interfaces)

`IActiveSubscriptionsReader` — lets the Liquidity module read active subscriptions without coupling to the Subscriptions module's internals:

```csharp
public interface IActiveSubscriptionsReader
{
    Task<IReadOnlyList<ActiveSubscriptionSummary>> GetActiveSubscriptionsAsync(
        Guid userId, CancellationToken ct = default);
}

public record ActiveSubscriptionSummary(
    string MerchantNameDisplay,
    string Cadence,
    decimal AverageAmount,
    string Currency,
    DateOnly NextExpectedDate);
```

Implemented by `FinanceSentry.Modules.Subscriptions` — a thin query on `IDetectedSubscriptionRepository`.

### Alert Extension (Alerts module)

1. Add `CashShortfall = "CashShortfall"` to `AlertType`.
2. Add `GenerateCashShortfallAlertAsync` + `ResolveCashShortfallAlertAsync` to `IAlertGeneratorService`.
3. Implement both in `AlertGeneratorService` with 24-hour silence window (active-alert dedup is the primary guard).

### Projection Engine

`CashFlowProjectionService.Project(balance, currency, subscriptions, fromDate, days)`:

```
for each day d in [fromDate+1 .. fromDate+days]:
  subtract all outflows whose NextExpectedDate == d and currency matches
  record running balance
return first day where balance < 0  (or null if none)
```

This is pure arithmetic — testable without any DB or DI.

### Sentinel Job

`LiquiditySentinelJob.ExecuteAsync()` (daily):

1. Get all distinct userIds from active bank accounts (via `IBankingAccountsReader`).
2. For each user:
   a. Load their active accounts (skip accounts with null balance).
   b. Load their active subscriptions (via `IActiveSubscriptionsReader`).
   c. For each account: run `CashFlowProjectionService.Project(...)`.
   d. If shortfall found: call `IAlertGeneratorService.GenerateCashShortfallAlertAsync(...)`.
   e. If no shortfall: call `IAlertGeneratorService.ResolveCashShortfallAlertAsync(...)`.

## Key Decisions

| Decision | Choice | Why |
|---|---|---|
| Where does the job live? | `Modules.Liquidity` (new module) | Isolation; the job's only purpose is projection — doesn't belong in BankSync or Subscriptions |
| Cross-module data access | `IActiveSubscriptionsReader` in Core.Interfaces | Same pattern as `IBankingAccountsReader`; no direct module coupling |
| Currency handling | v1 same-currency only | Avoids FX rate dependency in the hot path; safe to extend later |
| Projection window | 30 calendar days from today | Matches the issue spec; enough to catch monthly subscriptions |
| Shortfall threshold | balance < 0 | Explicit in the issue; user-configurable is deferred |
| Silence window | Active-alert dedup (primary) + 24h HasRecent after resolve | Prevents spam; matches LowBalance pattern |

## Implementation Sequence (Story Slice 1)

All three user stories are intertwined (the job, the projection, and the alert are one coherent slice), so this ships as one PR:

1. **Core**: add `IActiveSubscriptionsReader` + `ActiveSubscriptionSummary` record.
2. **Alerts**: add `CashShortfall` type; extend interface + implementation.
3. **Subscriptions**: implement `IActiveSubscriptionsReader`.
4. **Liquidity module**: create project, projection service, sentinel job, DI wiring.
5. **Tests**: unit tests for projection engine and job alert-trigger logic.
6. **API wiring**: register `LiquidityModule` in `Program.cs`.
