---
paths:
  - "backend/src/**/Application/**/*.cs"
  - "backend/src/**/Domain/**/*Summary*.cs"
  - "backend/src/**/*Dto*.cs"
  - "backend/src/**/*Reader*.cs"
  - "backend/src/FinanceSentry.Core/Utils/CurrencyConverter.cs"
---

# Currency / Money Aggregation

**Why it exists:** a raw `.Sum(x => x.Amount)` over accounts in different currencies added hryvnia to euros as if both were dollars - the "$28k monthly outflow" and "Government $71k top spending" bugs, fixed in #357. Full calculation rules: [docs/money-semantics.md](../../docs/money-semantics.md) (a PR changing money math updates it and `docs/finance-framework.md` in the same diff).
**Retire when:** aggregation over a native `Amount`/`Balance` fails the build (a typed `Money` value or an analyzer), so the convention is structural.

Accounts span currencies (Monobank=UAH, Revolut/AIB=EUR, IBKR/Binance=USD, …). **Never sum a native `Amount`/`Balance` across accounts.**

- **Convert once at the reader/query boundary**, where the account currency is in scope, via `FinanceSentry.Core.Utils.CurrencyConverter.ToUsd(amount, currency)` (the single conversion primitive; the FX refresh job feeds its rate table).
- **Every DTO that crosses an aggregation boundary carries a `…Usd` / `…InBaseCurrency` field** (e.g. `BankingTransactionSummary.AmountUsd`, `BankingAccountSummary.BalanceUsd`, `CryptoHoldingSummary.UsdValue`). Aggregations sum **only** that field, never the native one.
- When you add a new totals/summary path over transaction-level data, thread the account currency in and expose a converted field - do not sum native amounts and "fix it later".
- Unknown currency: `CurrencyConverter.ToUsd` falls back to 1:1. Use `CurrencyConverter.IsKnown(currency)` if you need to flag a total as approximate rather than trust a silent fallback.
