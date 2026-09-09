# Money Semantics — how every number is calculated

Source of truth for Finance Sentry's money math. **Any PR that changes one of these
behaviours must update this document in the same diff.** File references point at the
implementing code; when they disagree, the code is the bug or this doc is stale — fix one.

Last verified: 2026-08-31 (PR #531).

---

## 1. Balance semantics

`BankAccount.CurrentBalance` means different things by account type:

| Account type | CurrentBalance means | Sign |
|---|---|---|
| `checking` / `savings` | Own funds | positive = money you have |
| `credit` | Amount owed | positive = debt |

`BankAccount.CreditLimit` holds the credit line when the provider exposes one, null otherwise.

### Per provider

- **Monobank** (`MonobankHttpClient`, `MonobankAdapter`): the client-info `balance` field
  *includes* the credit limit on credit-enabled cards (`balance = own funds + creditLimit`).
  `MonobankHttpClient.ToStoredBalance` converts: accounts with `creditLimit > 0` store
  `creditLimit − balance` (amount owed) and are typed `credit` regardless of product name
  (a black/platinum card with a credit line is a liability account); accounts without a
  limit store the raw balance. Product-name mapping (`yellow` → credit, etc.) applies only
  when there is no credit line.
- **TrueLayer accounts** (`TrueLayerHttpClient`, `/data/v1/accounts/{id}/balance`): store
  `current` as-is (own funds; can be negative for an overdraft). `available` is fetched but
  not persisted.
- **TrueLayer cards** (`/data/v1/cards/{id}/balance`): `current` is already the amount
  owed — stored as-is with type `credit`; `credit_limit` → `CreditLimit`. Cards are a
  separate endpoint family: they never appear under `/data/v1/accounts`, are discovered at
  connect/reconnect (`FinalizeTrueLayerConnectCommand`) and by the scheduled sync's daily
  card-discovery pass (`ScheduledSyncService.DiscoverTrueLayerCardsAsync`), and are routed
  by `ProductType == "card"` (`TrueLayerAdapter.CardProductType`).

### Failure behaviour

A failed balance fetch **keeps the prior stored balance** — never zeroes it. Both the
Monobank path (rate-limit) and the TrueLayer path (`ScheduledSyncService`) follow this;
zeroing recorded phantom net-worth drops.

## 2. Liability sign convention

`FinanceSentry.Core.Utils.AccountBalanceMath` is the single authority:

- `IsLiability(accountType)` — true only for `"credit"`.
- `SignedForNetTotal(accountType, amount)` — negates credit balances.

**Aggregates** (net worth, currency totals, banking sleeve, wealth institution/card-group
totals, liquidity projections) always sum `SignedForNetTotal(...)`. **Per-account display**
keeps the raw positive value ("you owe X"), matching how banks present credit cards.

## 3. Currency conversion

- Convert **once, at the reader/query boundary**, via `CurrencyConverter.ToUsd(amount,
  currency)` — never sum native amounts across accounts (UAH + EUR is not USD).
- Every DTO crossing an aggregation boundary carries a `…Usd` field; aggregations sum only
  that field.
- Rate table: process-wide, refreshed daily at midnight UTC by the FX job
  (`Program.cs`), seeded with hardcoded fallbacks until the first refresh.
- **Unknown currency falls back 1:1.** Use `CurrencyConverter.IsKnown` to flag a total as
  approximate rather than trusting the silent fallback.
- **A rate may never be compared to another rate without a freshness check.** The seed table
  is live until the first refresh and survives any feed outage after it (the refresh job
  keeps the current table when the feed yields nothing), so `IsKnown` says nothing about
  whether a rate is current. Normalising magnitudes tolerates that — the total is approximate
  either way. Judging a bank's implied rate *against* the table does not: the measurement is
  the gap between them, so a drifted reference invents one. Gate such reads on
  `CurrencyConverter.AreRatesFresh(maxAge)` (`RatesUpdatedAtUtc` is null until the first
  refresh, and an ignored empty feed never moves it) and stand down rather than publish a
  figure. `FxSpreadDetectionJob` is the one such consumer today.

## 4. Transaction lifecycle (pending / posted / dedup)

- Dedup hash: `HMAC-SHA256(accountId|amount|date|description)`
  (`TransactionDeduplicationService`). Pending rows hash on `TransactionDate`; posted rows
  on `PostedDate`.
- **Settle-in-place**: if a posted candidate hashes identically to a stored *pending* row
  (Monobank holds keep their date when they clear), the stored row is flipped to posted
  (`ScheduledSyncService.PersistAndReconcileAsync`).
- **PendingReconciler**: a pending row whose posted twin exists under a *different* hash
  (date moved — the TrueLayer case) is retired (soft-deleted, `IsActive = false`). The twin is
  matched on account + amount + description, and the description goes through
  `SettlementDescriptionNormalizer` first, because a provider may also **rewrite the text** on
  settlement: AIB splices a `TxnDate: 02Sep2026` stamp into the booked copy that the pending
  copy never carried. Comparing raw text missed the twin, so neither row was retired and the
  payment counted twice in every figure derived from it. Normalization is deliberately narrow —
  only that stamp — since merging two genuinely distinct transactions is the worse failure.
- **Sync lookback overlap**: settled transactions keep their original timestamp, so a pure
  watermark fetch would never re-observe them once the watermark passes — every
  incremental sync therefore re-reads a trailing 7-day window (`ResyncLookbackDays` in
  both adapters). Dedup makes the overlap idempotent; it is what feeds settle-in-place
  and the reconciler.
- Net effect: a real purchase exists as exactly one active row; it may be `IsPending` for a
  few days, then becomes posted either in place or via retire-and-replace. A hold that
  takes longer than the lookback window to settle stays pending until a manual resync
  (reset the account's `LastTransactionSyncAt`).

## 5. Monthly inflow / outflow ("Spending (MTD)", "Monthly Outflow")

`MoneyFlowStatisticsService.GetMonthlyFlowAsync`:

- **Fetch window**: month-aligned — `MonthWindow.StartOfMonthsAgo(months)`, i.e. UTC
  midnight on the first of the month N back. `months` comes from the dashboard's selected
  range (3M/6M/1Y/All). So "3M" spans **three complete calendar months plus the one in
  progress**, and no bucket is ever a fragment of a month. (It used to be a raw
  `UtcNow.AddMonths(-n)`, which started mid-month and left the oldest bucket holding a
  handful of days — it charted as a collapsed bar and yielded a savings rate computed
  from a single day.)
- **Bucketing**: calendar UTC month on `PostedDate ?? TransactionDate`. The trailing
  bucket is month-to-date and is returned deliberately: it feeds the dashboard's
  month-to-date tiles. Callers must keep it out of month-over-month comparisons
  (see §7).
- **Included**: active transactions, **pending included** — a card hold is committed
  spending (excluding it made the month's outflow a fraction of reality).
- **Excluded**: internal transfers, two ways — (a) pair-matched via
  `TransferDetectionService` (cross-currency aware; pending rows participate, since
  pending money counts in the flow), (b) category-based `TRANSFER_IN` / `TRANSFER_OUT`.
  Note: credit-card repayments are transfers (moving money onto your own card), so they
  are rightly excluded — the *spending* is counted on the card itself when the provider
  exposes it (Revolut's TrueLayer integration does not: its card is invisible, so only
  the repayments are observable at all).
- **Loan and installment repayments are NOT transfers.** Monobank books them under the
  wire-transfer MCC 4829, the same MCC a genuine card-to-card transfer carries, so the MCC
  map alone buried the mortgage and every розстрочка repayment in `TRANSFER_OUT` and dropped
  them out of outflow (≈15% understated; issue #553). `LoanRepaymentClassifier` runs ahead of
  the MCC map on every path that categorizes a transaction and books them
  `LOAN_PAYMENTS` — real spending, and a repayment of principal is money that has left. It
  claims a debit on two signals: an installment wording
  (`InstallmentPlanRecognizer.IsInstallmentTransaction`), or a commitment key that belongs to
  an ACTIVE `installment`-kind `DetectedSubscription` **whose expected amount the debit is
  within 15% of** — the only way to reach a mortgage, whose description is a bare masked card
  number. The amount test is load-bearing, not a nicety: `MerchantNameNormalizer` strips the
  trailing group, so `516936******4992` keys as `516936` and every card issued on that BIN
  shares the key; without it a ₴200 transfer to a friend's card would book as a mortgage
  payment. Credits are never claimed, and a `subscription`-kind commitment (Netflix, a
  recurring transfer to a person) is never claimed, so genuine transfers on MCC 4829 stay
  excluded. A brand-new plan's first repayment is not yet detected, so it reads as a transfer
  until the next detection pass plus a re-categorization.
- **One ladder decides every category.** `ITransactionCategorizer` is the single ordered rule
  chain, and Monobank ingest, TrueLayer ingest and the `recategorize` backfill all run it and
  nothing else — a backfill can therefore only confirm or correct an ingest decision, never
  reverse one. The order, highest first: the runtime-editable `merchant_keywords` bridge (an
  admin keeps the last word on a wording); the loan/installment rule above; the provider's own
  category (a bank calling "To Go Sushi" *Restaurants* beats the directional prefix below);
  the directional-transfer / savings-jar description (which outranks the MCC map because
  Monobank tags jar operations with the charity MCC 8398); then the MCC map. A rung resolving
  to `UNCATEGORIZED` has not claimed the row, so the ladder continues past it; when no rung
  claims it the result is null, which keeps a backfilled row eligible for a provider re-fetch.
  Before the ladder was shared, the three paths disagreed: the backfill re-buried savings jars
  as charity spend and re-claimed admin keyword overrides on any MCC-bearing row, and TrueLayer
  ingest never consulted the loan rule at all.
  Sharing the ladder is only half the contract — **every path must also feed a rung the same
  shape**. The provider rung takes a canonical key, never a bank's raw wording: ingest maps the
  live classification, and the backfill maps the stored `SourceCategory` back through the same
  `TrueLayerCategoryMapper` (`ToSourceCategory` / `MapStored` are inverses kept side by side).
  Hand that rung a raw string and canonical-key validation rejects it, the rung is skipped on
  that path alone, and the directional-prefix rung below re-labels a restaurant `TRANSFER_OUT` —
  out of outflow entirely, and unrecoverable, since the re-fetch pass skips rows that already
  carry a `SourceCategory`.
- Outflow = sum of `debit` amounts, inflow = sum of `credit` amounts, per currency, plus
  USD-converted fields. Transactions on deactivated accounts resolve to currency
  `"UNKNOWN"` (converted 1:1).
- **Not cached** — recomputed per `/dashboard/aggregated` request. The dashboard polls
  every 5 minutes; the transaction-ledger stat card fetches once at page load.

### 5.1 Counterparty flows (family clearing house, investment routing)

`CounterpartyClassificationService` runs **once per request** and its result is handed to
both the money-flow and the top-categories reader, so a movement can never be spending in
one and a transfer in the other.

- A transaction belongs to a counterparty when a seeded rule matches its description or
  merchant name (case-insensitive substring). First counterparty whose rule matches wins.
- Matched transactions leave the normal pass entirely: they are excluded before
  transfer pair-matching, so they cannot be double-excluded or double-counted.
- **Classification is per DIRECTION, gross — there is no netting between the two
  directions of the same counterparty.** Every credit from a counterparty is inbound and
  every debit to it is outbound, in full, even in the same month. Netting the pair was the
  original bug in a new costume: a month with ₴18k of rent in and ₴13k of support out
  reported ₴5k of income and *no spending at all*.
- The counterparty's **flow role** decides where each direction lands:

  | Role | Outbound | Inbound |
  |---|---|---|
  | `family_support` | `OutflowUsd` + the `FAMILY_SUPPORT` category (real spending) | `InflowUsd` (rent is income) |
  | `investment` | `InvestedOutflowUsd` only — never outflow or spend | neither: capital coming back is not earnings |
  | `household` | `OutflowUsd` (a bill paid as a transfer, e.g. the mortgage) — but **not** `FamilySupportOutflowUsd` and not the `FAMILY_SUPPORT` category | `InflowUsd` (a refund of a bill is money back) |
  | `self_routing` | nothing — the user's own money mid-hop (e.g. Revolut → mom → Monobank, whose legs share no statement words so pair detection can't see them) | nothing |

  Rules can carry an optional account-currency filter, and a currency-scoped match beats a
  generic one — «Від: Людмила Сичова» in UAH is rent (`family_support` income), in EUR it is
  the same wording on a routing hop (`self_routing`, excluded).

  `self_routing` also covers the user's **own** EUR accounts (`Own accounts (EUR)`, M019): an
  AIB → Revolut hop reads `*MOBI DENYS SYCHOV IE…` on one statement and `Payment from Denys
  Sychov` on the other. The legs share nothing pair detection can key on and settle on
  different days, so the credit read as INCOME and inflated gross income and the savings rate
  every month it happened. Counterparty classification is per direction, so one rule retires
  both legs; the direction-blind `denys sychov ie` → `TRANSFER_IN` keyword that used to patch
  the inbound leg (and mislabelled the outbound one) is dropped in the same migration.

  Known gap: `household` outbound joins outflow but no spending *category* — top-categories
  only emits a synthetic row for `family_support` — and it always lands as discretionary,
  never committed (#560 owns commitment matching for manual obligations).

- Output is ordered by (month, counterparty name) so re-running over a fixed window
  reproduces the same buckets in the same order.
- Each month's counterparty flows are emitted as **one synthetic USD row per month**
  (native amounts zero — the classification is already currency-normalised), never
  folded into a per-currency bucket's USD figures.

### 5a. Committed vs discretionary outflow

`OutflowUsd` is partitioned into `CommittedOutflowUsd` + `DiscretionaryOutflowUsd`; the two
always sum back to it, and no figure is committed unless it is already in `Outflow` (so
transfers are in none of the three).

**`CommittedOutflowRules` (`BankSync/Application/Services/CommittedOutflowPolicy.cs`) is the
only place the definition lives.** Consumers load it once per user via `ICommittedOutflowPolicy`
and ask it; none of them re-derives a rule. An outflow is **committed** when ANY rule fires:

- **(a) an active detected commitment** — the key derived by
  `CommitmentKeyResolver.Resolve(MerchantName, Description, Amount, Mcc)` is the key of one of
  the user's `DetectedSubscription` rows whose status is `active`, read through
  `IActiveSubscriptionsReader.GetActiveCommitmentMerchantKeysAsync`. Both kinds count, and each
  is keyed the way the detector keys it: recurring services by
  `MerchantNameNormalizer.NormalizeDetectionKey`, installment (розстрочка) plans by
  `InstallmentPlanRecognizer.PlanKey` (`installment:{merchant}:{roundedAmount}`). The resolver
  mirrors `SubscriptionDetectionJob`'s own routing between the two detectors — that is what
  keeps the stored key and the matched key from drifting apart. A plan's identity includes its
  rounded monthly amount, so concurrent plans at one shop stay distinct and only the plan the
  user actually holds is claimed.
- **(b) a committed category** — `RENT_AND_UTILITIES` or `LOAN_PAYMENTS`. Rent is the largest
  fixed outflow in the book and the detector can never see it: the same payee for the same
  amount every month is not a merchant recurrence signature. A loan payment is a debt
  obligation whether or not a plan was detected behind it. The set is deliberately just these
  two; `GENERAL_SERVICES` and `TRANSPORTATION` mix subscriptions with impulse spend.
- **(c) a counterparty obligation** — the flow role assigned by the counterparty classification
  (§5.1, spec 044) is `family_support` or `household`. Applied per ROLE, not per debit, because
  counterparty transactions are filtered out of the per-debit pass and re-enter as one
  synthetic USD row per month. `investment` and `self_routing` are absent because they are not
  spending at all; a counterparty saved with no role is spending that names no obligation and
  falls to discretionary.
- **(d) a user pin** — the debit's `MerchantNameNormalizer.NormalizeDetectionKey` is one of the
  merchant keys the user pinned (`committed_merchant_pins`, unique per `(UserId, MerchantKey)`).
  This is the escape hatch for the obligations only the user knows about: a standing payment to
  a person, a gym with a lock-in, a service billed too irregularly for the detector to promote
  it. Keyed on the **detection key**, not on `CommitmentKeyResolver.Resolve`, because a pin
  names a MERCHANT and must claim that merchant's charges even when an individual row is shaped
  like an installment repayment (which the resolver keys `installment:{merchant}:{amount}`, a
  form no merchant pin can equal). A debit whose merchant cannot be named collapses to the
  `unknown` key and is never claimed by a pin — otherwise one pin would take the whole unnamed
  tail of the book; the write path (`CommittedMerchantKey.Derive`) refuses to mint that key and
  the rule refuses to honour it. Managed via `GET`/`POST`/`DELETE /api/v1/committed-merchants`
  and the `committed_merchants` MCP tool. Pinning is idempotent: two spellings of one merchant
  normalize to one key, and two concurrent pins of it both report the row that exists.
  Unpinning accepts either the merchant text or the listed `merchantKey` — both derive to the
  same key, because `NormalizeDetectionKey` is a fixed point over its own output.
  **A pin is an exact key match, not a substring search**: a row that carries no
  `MerchantName` keys off its whole description, so a pin on `Telemart` does not claim
  `Щомісячний платіж telemart - monomarket`. Looser matching would let a pin on a common word
  swallow unrelated spend; the cost is that pins are ineffective on description-only rows.

**Discretionary** = every other non-transfer outflow. Derived as
`OutflowUsd − CommittedOutflowUsd` (and, on the synthetic row, `expense − committed`) so the
partition is exact; converting the two subsets independently would let rounding pull them off
the total.

- **Currency**: the committed native sum is per (month, currency) bucket and is converted
  with `CurrencyConverter.ToUsd` at the same reader boundary as `OutflowUsd`. Commitments
  are billed in UAH, EUR and USD, so only the `…Usd` fields may be added across rows.
- **Status is point-in-time**: cancelling a subscription today reclassifies its past charges
  as discretionary. The split describes today's commitments, not history.
- **Known under-count**: a full early payoff ("Повне погашення") is not keyed as a plan — the
  detector uses payoffs only to mark a plan completed, and a completed plan is no longer
  `active` — so a payoff reads as discretionary unless its category carries it. Ordinary spend
  — groceries, restaurants, clothes — is discretionary by construction, which is the point.
- **Known over-claim**: rule (b) inherits whatever the ingest ladder (#553) put in its two
  keys, and `RENT_AND_UTILITIES` is wider than rent — the telecom MCC range 4812–4900 and the
  `top-up` keyword land there, so an ad-hoc mobile top-up reads as committed alongside the
  monthly phone plan. Accepted rather than carved out: a statement line carries nothing that
  tells a plan payment from a discretionary top-up, and narrowing by MCC inside the policy
  would put a second categorization opinion next to the ladder that owns it. The amounts are
  small; rent, utilities and loans are not.
- **Coverage**: measured at 7.9% of outflow under rule (a) alone (comment on issue #538,
  2026-09-03), and ~18% once #553 restored the mortgage and the monomarket repayments to
  outflow — both below the 40% bar #538 set for charting the split, which is why the figures
  are on the API and not on the dashboard. Rules (b) and (c) are the widening; the share has
  NOT been re-measured on production since, and #554 is not done until it has been. See
  `specs/045-committed-vs-discretionary/`, `specs/553-loan-repayment-categorization/` and
  `specs/554-committed-outflow-policy/`.

## 6. Top spending categories

`MerchantCategoryStatisticsService`: same filters as monthly flow (active, pending
included, transfers excluded, debits only, USD-converted) over the same month-aligned
window (`MonthWindow.StartOfMonthsAgo`), but **flat — not month-bucketed**. Displayed as
"Top Spending Categories (3M)" etc., following the dashboard's selected range.

Unlike the bar charts (§7) this **includes the in-progress month**. A composition is not a
period-over-period comparison, so a partial month does not distort it the way it distorts
a bar sitting next to complete ones — and dropping the freshest spending from "where does
my money go" would be a real loss.

## 7. Month-bucketed charts vs. month-to-date tiles

Frontend-only (`dashboard.computed.ts`). The in-progress month appears in exactly one
place, and the split is deliberate.

**Charts plot complete calendar months only** — both *Income vs Spending* and *Monthly
Savings Rate* read the same `completeMonths` window, so they always share an x-axis. A
partial month as a bar next to complete ones is an apples-to-oranges comparison: income
reads as collapsing, and the savings rate swings to absurd magnitudes (the old chart read
-500,000%), because salary posts once — often on the last day — so until then the month
holds a full run of spending against stray small credits. Savings rate additionally drops
completed months with zero inflow, for the same divide-by-near-zero reason.

**Month-to-date tiles carry the in-progress month**, labelled `(MTD)`:

- *Income (MTD)* / *Spending (MTD)*: current-month totals, compared against the average of
  the trailing 3 complete months **prorated by day-of-month elapsed** — without proration
  a figure two days into the month always reads as a collapse.
- *Savings rate (MTD)*: withheld (shows `—`) until month-to-date inflow reaches
  `INCOME_LANDED_FRACTION` (50%) of a normal month's income. Below that the raw rate is
  technically correct and completely misleading. Compared in **percentage points** against
  the trailing complete months, since a rate is scale-free and is not prorated.
- The `cmn-stat-card` `delta` input drives colour and arrow off its **sign**, so the number
  passed is "how good is this", not "which direction did it move" — for spending those are
  opposites, and the wording (`over pace` / `under pace`) carries the direction instead.

This is the same split Binance and IBKR use: the current period is a tile with a
comparison; the bars are closed periods.

## 8. Net worth

- **Headline stat** ("Net Worth" card): computed **live** per request —
  banking (signed per §2, USD per §3) + crypto holdings + brokerage holdings
  (`DashboardQueryService`).
- **History chart**: daily `net_worth_snapshots` rows, one per (user, UTC date),
  **upserted** — refreshed by every successful account sync (`FirstSyncSnapshotTrigger`)
  and by the 01:00 UTC Hangfire backstop job (`NetWorthSnapshotJob`). The newest point
  therefore tracks the live position through the day. Headline and last chart point can
  still differ by minutes, not by a day.
- **Carry-forward**: a sleeve (banking/brokerage/crypto) whose feed hasn't synced within
  36h, or that drops to exactly $0 from a positive value, is treated as a failed sync — the
  previous day's value is carried forward and the sleeve is listed in `StaleSleeves`
  (`NetWorthSnapshotService`). The baseline is the latest snapshot *strictly before* the
  snapshot date, so a same-day refresh never carries forward from itself.
- **Backfill** (`NetWorthSnapshotBackfillService`, on boot): fills missed days with
  *current* balances — a downtime gap renders as a flat line, not real history.

## 9. Known approximations (accepted)

- Everything is UTC; no user-timezone normalization of transaction dates or month edges.
- Unknown currencies convert 1:1 (§3).
- A pending transaction and its posted twin can both be active between the twin's arrival
  and the account's next sync — a transient double-count window of one sync cycle.
- A hold that settles with a materially different amount or description than it was
  authorized with produces a new posted row; the stale pending twin is only retired if
  the amount+description reconciler key still matches.
- Backfilled snapshot days are not historical truth (§8).
- The month-to-date pace baseline (§7) prorates a monthly average linearly by elapsed
  days. Real spending is lumpy — rent lands on the 1st, salary on the last day — so pace
  is directionally right rather than exact. A true same-day-last-month comparison would
  need day-level cumulative flow from the backend, which is not built.
