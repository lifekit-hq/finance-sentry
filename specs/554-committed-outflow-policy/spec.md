# Feature Specification: One Committed-Outflow Policy — Subscriptions, Rent, Loans, Family Support, Pins

**Feature Branch**: `goal/fs-554-committed-policy-2026-09-03`

**Created**: 2026-09-07

**Status**: US1 implemented; US2 (user pins) not started

**GitHub Issue**: #554

## Context

Spec 045 shipped a committed/discretionary partition of `OutflowUsd`, defining *committed*
as "the debit's commitment key matches an active `DetectedSubscription`". Issue #538 then
stopped itself on its own coverage gate: over Jun–Aug 2026 that definition claims **7.9%** of
outflow, and only ~18% even after #553 restored the mortgage and the monomarket installments to
outflow. A chart that files four-fifths of spending as "discretionary" is worse than no chart,
so #538's dashboard slice stays unshipped until the definition is honest.

The definition cannot get there by improving the detector. The largest fixed monthly outflows
carry no merchant-recurrence signature it can see:

| outflow | today | why the detector misses it |
|---|---|---|
| rent `To Mario Scalas` 1,275 EUR/mo, `RENT_AND_UTILITIES` | discretionary | same payee, same amount, but never promoted to a `DetectedSubscription` |
| family support (`мама`, `Ліза ❤️`, `Liudmyla Sychova`) | a counterparty expense since #429 | counterparty-classified, so it never reaches the per-debit matcher at all |
| utilities, one-off `LOAN_PAYMENTS` rows | discretionary | categorized correctly by #553, but the category was never a committed signal |

Widening this is explicitly "a separate ticket, not a silent workaround" (issue #538).

## The definition to adopt

An outflow is **committed** when it satisfies ANY of:

- **(a) active commitment** — its `CommitmentKeyResolver.Resolve` key is the key of one of the
  user's active `DetectedSubscription` rows (subscriptions and installment plans), exactly as
  045/US1b already matches.
- **(b) committed category** — its category is `RENT_AND_UTILITIES` or `LOAN_PAYMENTS`.
- **(c) counterparty obligation** — it is a counterparty-classified flow whose role is real
  spending on a standing obligation: `family_support` or `household` (spec 044 / #429).
- **(d) user pin** — the user pinned its merchant key as committed.

Everything else that is already in `Outflow` is **discretionary**. Transfers, investment
routing and self-routing are in neither, because they are in no outflow.

---

## User Scenarios

### [US1] One policy decides committed vs discretionary — rules (a), (b), (c)

**As** the dashboard reader, **I want** the committed share of my spending to include rent,
utilities, loan payments and the money I send my family, **so that** the split describes
obligations I cannot cancel this month rather than only the handful of services a recurrence
detector happened to spot.

**Acceptance**

1. A single `ICommittedOutflowPolicy` decides committed vs discretionary. Its per-user rule set
   answers both questions the reader asks — per debit, and per counterparty flow role — and the
   rules are documented in its XML doc.
2. `MoneyFlowStatisticsService` calls the policy; it no longer reads
   `IActiveSubscriptionsReader` or re-derives a rule of its own.
3. Rule (b): a `RENT_AND_UTILITIES` or `LOAN_PAYMENTS` debit is committed with no
   `DetectedSubscription` involved.
4. Rule (c) is wired through #429's counterparty classification — the `family_support` and
   `household` outflow of the month's synthetic counterparty row is committed, and the row's
   remainder (a counterparty carrying no role) stays discretionary. There is no second
   family-flow heuristic anywhere.
5. `CommittedOutflowUsd + DiscretionaryOutflowUsd == OutflowUsd` on every row, including the
   synthetic counterparty row.
6. Fixtures shaped like the real data — monthly rent to the same payee, family support, a
   mortgage-shaped and a monomarket-shaped repayment, a Netflix-style subscription, a one-off
   shop — each land on the right side of the split, including a cross-currency case that would
   fail under a native sum.

### [US2] Rule (d) — user-pinned committed merchants (NOT built)

**As** the user, **I want** to mark a merchant as committed, **so that** an obligation only I
know about (a standing payment to a person, a gym I am locked into) counts as committed.

**Acceptance**

1. A per-user list of merchant keys persisted in the BankSync module, keyed by
   `MerchantNameNormalizer.NormalizeDetectionKey`.
2. An endpoint to list/add/remove entries, and an MCP tool over the same commands.
3. Rule (d) reads the list in `CommittedOutflowPolicy` — the same rule set, one more clause.
4. Backend unit + contract tests; `docs/money-semantics.md` §5a updated.

---

## Out of Scope

- The #538 dashboard chart (clauses 2–5 of that issue). It stays unshipped; re-file it as a
  fresh ticket only after the coverage gate is re-measured on production.
- `Liquidity/Application/Services/CashFlowProjectionService` — a different question
  (what will I owe next), not this one (what did I owe last month).
- Re-running the #538 AC1 coverage query: it needs production data, which no sandbox has.

## Non-Functional

- Money rule: only `…Usd` figures may be summed across rows; the committed native sum is
  converted once at the reader boundary where the account currency is in scope.
- `dotnet build backend/` with zero new warnings; `dotnet test backend/` green.
