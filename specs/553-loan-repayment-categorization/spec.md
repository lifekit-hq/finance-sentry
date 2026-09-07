# Feature Specification: Loan and Installment Repayments Categorized as LOAN_PAYMENTS

**Feature Branch**: `goal/fs-553-loan-categorization-2026-09-03`

**Created**: 2026-09-07

**Status**: US1 implemented

**GitHub Issue**: #553

## Context

Monobank books loan and installment repayments under MCC **4829** ("Money Orders – Wire
Transfer") — the same MCC it uses for genuine card-to-card transfers. `MccRangeClassifier`
maps 4829 straight to `TRANSFER_OUT`, and `CategoryKeys.IsTransfer` then drops those rows from
every outflow view: `MoneyFlowStatisticsService` (monthly flow, savings rate, the
committed/discretionary split from 045), top categories, and the dashboard tiles that read
them. Outflow is understated by roughly 15% and the largest fixed commitment in the book — the
mortgage — is invisible.

Production evidence (Jun–Aug 2026):

| debit | mcc | category today | should be |
|---|---|---|---|
| `516936******4992` ₴14,060.96/mo (the `Іпотека` plan in `detected_subscriptions`, active, 2024-05 → 2036-05) | 4829 | TRANSFER_OUT | LOAN_PAYMENTS |
| `Платіж FOXTROT- monomarket` ₴144.95, `Платіж Fopi- monomarket` ₴649.95, `Платіж ТОВ Алло - monomarket` ₴2,999.95 | 4829 | TRANSFER_OUT | LOAN_PAYMENTS |
| `Щомісячний платіж telemart - monomarket` ₴6,499.85 | (other) | LOAN_PAYMENTS | LOAN_PAYMENTS ✓ |
| `Переказ на картку`, `Ліза ❤️`, `мама`, `Liudmyla Sychova` | 4829 / — | TRANSFER_OUT | TRANSFER_OUT ✓ |

### What #581/#582 already fixed, and what it did not

PR #582 spliced the runtime-editable merchant-keyword bridge in front of the MCC map on the
Monobank ingest path, and migration `M015_RepairInstallmentCategories` repaired the stored rows
whose *description* carries a Ukrainian installment wording («погашення», «щомісячний платіж»,
`monomarket`, `Платіж Pandora`). That covers the monomarket rows above.

It does **not** cover the mortgage. `516936******4992` is a masked card number — it carries no
wording a keyword could match, and no keyword ever will without also claiming every genuine
card-to-card transfer. The mortgage is only recognisable through the thing that already knows
it is a repayment obligation: `SubscriptionDetectionJob` stores it as an **`installment`-kind**
`DetectedSubscription` precisely because its merchant key is a masked PAN
(`MaskedPan.IsLikely` → `SubscriptionKinds.Installment`).

The second gap is structural: keyword rows are runtime data, so a fresh database or a deleted
row silently reopens the hole. The rule belongs in code, in the categorization chain, next to
the recognizer that already defines what an installment repayment looks like.

---

## User Scenarios

### [US1] Loan repayments are categorized LOAN_PAYMENTS at the source (P1)

A debit that the installment recognizer identifies as an installment/loan repayment, or whose
commitment key is the key of an active `installment`-kind `DetectedSubscription`, is
categorized `LOAN_PAYMENTS` — both on the Monobank ingest path and when existing rows are
re-categorized. The rule takes precedence over the MCC-4829 → `TRANSFER_OUT` mapping, and lives
in one place in the categorization chain, never in a consumer.

**Acceptance Scenarios**:

1. **Given** a debit `Платіж ТОВ Алло - monomarket` with MCC 4829, **When** it is categorized,
   **Then** its category is `LOAN_PAYMENTS`, not `TRANSFER_OUT`.
2. **Given** a debit `516936******4992` with MCC 4829 and an active `installment`-kind
   `DetectedSubscription` stored under that key for about that amount, **When** it is
   categorized, **Then** its category is `LOAN_PAYMENTS`.
3. **Given** a debit `Переказ на картку` / `Ліза ❤️` / `Поповнення «Поточний»` with MCC 4829 and
   no matching active installment plan, **When** it is categorized, **Then** it stays
   `TRANSFER_OUT`.
3b. **Given** a ₴200 debit to `516936******1111` — a *different* card on the same BIN as the
   mortgage, and therefore carrying the *same* stored plan key, since
   `MerchantNameNormalizer` strips the trailing group — **When** it is categorized, **Then** it
   stays `TRANSFER_OUT`, because its amount is nowhere near the plan's.
4. **Given** a **credit** whose description carries an installment wording (an installment
   refund), **When** it is categorized, **Then** the loan rule does not claim it — the rule is
   about outflow, and a credit on MCC 4829 is money coming in.
5. **Given** a merchant key that matches an active `subscription`-kind commitment (Netflix, a
   recurring transfer to a person), **When** it is categorized, **Then** the loan rule does not
   claim it — only `installment`-kind plans are repayment obligations.
6. **Given** stored rows that were categorized `TRANSFER_OUT` before this rule existed, **When**
   the existing recategorization path runs (`recategorize` verb →
   `TransactionRecategorizationService`), **Then** they are re-categorized `LOAN_PAYMENTS`, so
   history changes too and not only new syncs.
7. **Given** a mortgage-shaped and a monomarket-shaped debit in a month, **When** monthly flow
   is computed, **Then** both are in `Outflow`/`OutflowUsd` (no longer excluded as transfers)
   and, when their commitment key is an active commitment, in `CommittedOutflowUsd`.

**Out of scope for this feature**: no special-casing in `MoneyFlowStatisticsService`, the
dashboard, or the top-categories reader — the category is wrong at the source and every
consumer reads `MerchantCategory`. No data migration: the recategorization path is the backfill
mechanism the issue names, and `M015` already exists for the wording-matched half.

---

## Success Criteria

- `dotnet build backend/FinanceSentry.sln -c Release` — zero warnings.
- `dotnet test backend/FinanceSentry.sln --filter "Category!=Integration"` — green.
- Unit tests pin each side of the MCC-4829 split with description-shaped fixtures: a
  mortgage-shaped masked card, a monomarket-shaped repayment, and plain transfers.
- `MoneyFlowStatisticsTests` covers a mortgage-shaped and a monomarket-shaped debit landing in
  outflow and in committed.

**Post-merge check (manual, not part of the completion contract)**: re-run the #538 AC1 query on
production — the mortgage appears in monthly outflow and the committed share moves from ~8% to
~18%.
