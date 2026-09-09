# 044 — Hygiene Sentinels: Implementation Plan

## Architecture

Each detector is a Hangfire job in `FinanceSentry.Modules.BankSync/Infrastructure/Jobs/`. They consume:
- `IAlertGeneratorService` (Core) — alert emission + dedup
- BankSync's own `BankSyncDbContext` (transactions / accounts) for US2, US3, US4
- A new Core port `ISubscriptionHygieneSummaryReader` (US1) — bridging to Subscriptions module

Thresholds are read from `IConfiguration` under the `HygieneSentinels:*` keys. Each job is registered in `BankSyncModule.JobRegistrar` as a daily recurring job.

New alert types (`PriceHike`, `DuplicateCharge`, `CategorySpike`, `FxSpread`) are added to `AlertType`, `IAlertGeneratorService`, `AlertGeneratorService`, `CompanionEventKind`, and `MaterialityPolicy` in one PR each.

## Cross-module boundary (US1)

`PriceHikeDetectionJob` lives in BankSync which only references Core. To access `DetectedSubscription` data without a module-to-module reference:
- New port in Core: `ISubscriptionHygieneSummaryReader` → `SubscriptionHygieneSummary` record
- Adapter in Subscriptions module: `SubscriptionHygieneSummaryReader` queries `SubscriptionsDbContext.DetectedSubscriptions` directly (efficient single cross-user query, bypasses per-user repo method)
- Registered in `SubscriptionsModule` alongside existing `IActiveSubscriptionsReader`

This follows the same pattern as `IActiveSubscriptionsReader` / `ActiveSubscriptionsReader`.

## The hike baseline (US1)

Reading recurrence from `detected_subscriptions` rather than re-deriving it means the sentinel
can only see what detection chose to record — and detection recorded no evidence a price had
ever changed. `SubscriptionDetectionJob` keeps only the amount cluster holding the most recent
charge, and the cluster tolerance (15%) equals the sentinel threshold (15%), so the two gates
excluded each other: a hike large enough to fire split the new price into a cluster of one,
which failed the three-occurrence gate and dropped the subscription off the list entirely; a
hike small enough to stay clustered was diluted by its own charge in the average it was being
compared against (a 15% step over two prior charges measures 9.5%). No charge series could
produce an alert.

Decision: detection reports the displaced cluster's price as `PreviousAmount` while the new
price is still new — that is, until the current cluster has `MinOccurrences` charges of its
own — and the sentinel measures against `SubscriptionHygieneSummary.HikeBaseline`
(`PreviousAmount ?? AverageAmount`). The displaced cluster also counts toward the occurrence
gate, which is what keeps a repriced subscription on the list at all.

Why not simply lower the threshold: it would only ever catch hikes small enough to survive
clustering, leaving the large ones — the ones worth alerting on — permanently invisible.

A displaced cluster is a repricing, and not two different things, only when all of:
- the merchant billed exactly two prices — a third cluster is an outlier or a third plan, and
  taking the nearest of several would let one stray charge shadow the price really replaced;
- those two form a clean chronological step — concurrent plans interleave in time;
- the step is within `MaxPriceStepRatio` (2×, so Claude Pro €22 → Max €110 stays a plan switch);
- the old price has at least `MinBaselineCharges` (2) charges — a prorated or promotional first
  month is one charge with zero variance, and would otherwise alert on the merchant's own
  onboarding;
- the old price was itself stable under the CV gate.

Each guard fails closed: no baseline, and the row behaves exactly as it did before this change.

Tying the baseline's lifetime to `MinOccurrences` rather than to a time window bounds
re-alerting to one further monthly cycle past the 30-day alert silence — the baseline clears on
the third charge at the new price.

## One merchant, one unit (US1)

Detection groups a merchant's charges by normalized name alone, but a user's accounts span
currencies — Monobank UAH, Revolut/AIB EUR, a UK card GBP — so one group can hold amounts in
several units. Everything downstream of that group is a comparison between two amounts: the
15% cluster tolerance, the CV stability gate, the `MaxPriceStepRatio` guard, and the
`HikeBaseline` the sentinel divides by. Comparing across units silently compares two things.

The damage is not theoretical and it is not the large gaps — ₴449 next to €10.99 is a 40×
ratio that `MaxPriceStepRatio` already refuses. It is the pairs that sit *inside* the guards:
GBP→EUR (≈1.18) and GBP→USD (≈1.27) both clear the 15% cluster tolerance, stay under the 2×
step ratio, and form a clean chronological step, so moving a subscription from a GBP card to a
EUR one manufactures a textbook repricing. The sentinel then fires a confident, correctly
dedup'd 20% price-hike alert for a rise no merchant charged — and symmetrically, a move in the
other direction masks a real hike behind a fabricated cut.

Decision: a merchant's price series is the charges in the currency it bills **today**
(`InCurrentBillingCurrency`, applied before `SplitAtPriceStep`). A retired currency leaves the
series exactly as a retired plan does — it is no longer what the merchant bills.

Which currency counts as today's takes the same minimum evidence a price does
(`MinBaselineCharges`, the file's existing "one charge is not a price" constant). Without that
gate the partition is a far worse bug than the one it fixes: a single purchase abroad at a
merchant the user also subscribes to would retire nine months of established charges and
delete the subscription outright. With it, the newcomer has to bill twice before it displaces
anything, and until then the row keeps updating in the currency it is still billed in rather
than going stale. A same-day tie between two established currencies carries no signal, so it
is broken on the currency name — arbitrary, but the source is an unordered query result and
the row must not differ between runs over identical data.

Why not convert to a common unit through `CurrencyConverter.ToUsd`, which the backend rules
otherwise mandate for cross-currency comparison: that rule governs *normalising magnitudes*,
where an approximate rate degrades a total. Here the converted number is fed to a 15% cluster
tolerance and a 15% hike threshold, and the converter's table is approximate by contract — it
falls back to a hardcoded seed that a feed outage leaves standing, drifting the same order as
the tolerance itself. Converting would hand a stale rate the power to manufacture the very
step this guard exists to refuse, which is the mistake US4 already had to undo.

Cost, accepted and bounded: a subscription mid-move carries only the charges in whichever
currency is established, so between the newcomer's second charge and its third it can sit
under `MinOccurrences` — one monthly cycle. Detection stops *reporting* the merchant for that
cycle; it does not delete it, so the row stays on the list at its last known price, and if the
charges really have stopped `MarkStaleAsPotentiallyCancelledAsync` flips it to *potentially
cancelled* after ~45 days. The row it produced before this change was an average across two
units, so nothing true is lost either way.

The restated amounts have to carry their unit with them: `UpdateFromDetection` assigns
`Currency` alongside them, because `GetSubscriptionSummaryQuery` and
`GetInstallmentFxImpactQuery` run `ToUsd(amount, Currency)` over the row. A row left labelled
GBP while its amounts turned into euros is not a cosmetic mislabel — it misstates the user's
monthly spend total by the whole GBP/EUR rate.

Not affected: `DetectInstallments` groups by `(merchant, rounded plan amount)`, so two
currencies only share a plan when the same *number* is billed in both — and then the ratio is
1:1 and no step can be fabricated. It is bounded by its own grouping key, not by luck.

Known limits, both consequences of reading detection's output rather than the raw series:
- `AmountClusterTolerance` sets the floor on what a hike can be. A step under 15% never leaves
  its cluster, so it has no baseline and is still measured against a diluted average; lowering
  `HygieneSentinels:PriceHikeThreshold` below 15% buys less than it appears to.
- An annual subscription cannot produce a baseline: two prior charges at the old price do not
  fit the 13-month detection lookback. Annual repricing stays invisible to this sentinel.
- `OccurrenceCount` counts the displaced charges too, so it rises while a step is live and
  returns to the current-cluster count once the price settles.

## [US1] Price hike — files touched
- NEW `backend/src/FinanceSentry.Core/Interfaces/ISubscriptionHygieneSummaryReader.cs`
- NEW `backend/src/FinanceSentry.Modules.Subscriptions/Application/Services/SubscriptionHygieneSummaryReader.cs`
- EDIT `backend/src/FinanceSentry.Modules.Subscriptions/SubscriptionsModule.cs` — register adapter
- EDIT `backend/src/FinanceSentry.Modules.Alerts/Domain/AlertType.cs` — add `PriceHike`
- EDIT `backend/src/FinanceSentry.Core/Interfaces/IAlertGeneratorService.cs` — add method
- EDIT `backend/src/FinanceSentry.Modules.Alerts/Application/Services/AlertGeneratorService.cs` — implement
- EDIT `backend/src/FinanceSentry.Modules.Companion/Domain/CompanionEventKind.cs` — add `PriceHike`
- EDIT `backend/src/FinanceSentry.Modules.Companion/Application/Services/MaterialityPolicy.cs` — map it
- NEW `backend/src/FinanceSentry.Modules.BankSync/Infrastructure/Jobs/PriceHikeDetectionJob.cs`
- EDIT `backend/src/FinanceSentry.Modules.BankSync/BankSyncModule.cs` — register + schedule daily
- NEW `backend/tests/FinanceSentry.Tests.Unit/BankSync/Infrastructure/PriceHikeDetectionJobTests.cs`

### Baseline follow-up (the hike a merchant actually charges)
- EDIT `backend/src/FinanceSentry.Modules.BankSync/Infrastructure/Jobs/SubscriptionDetectionJob.cs` — `SplitAtPriceStep` / `PriceSeries`
- EDIT `backend/src/FinanceSentry.Core/Interfaces/ISubscriptionDetectionResultService.cs` — `DetectedSubscriptionData.PreviousAmount`
- EDIT `backend/src/FinanceSentry.Modules.Subscriptions/Domain/DetectedSubscription.cs` + `SubscriptionsDbContext.cs` — persist it
- NEW `backend/src/FinanceSentry.Modules.Subscriptions/Migrations/20260906000000_M006_AddPreviousAmount.cs` (+ Designer, snapshot)
- EDIT `ISubscriptionHygieneSummaryReader.cs` — `PreviousAmount` + `HikeBaseline`; reader projects it
- NEW `backend/tests/FinanceSentry.Tests.Unit/BankSync/Infrastructure/PriceHikeSentinelPipelineTests.cs` — detect → persist → read → alert

### Single-currency follow-up (a change of unit is not a change of price)
- EDIT `backend/src/FinanceSentry.Modules.BankSync/Infrastructure/Jobs/SubscriptionDetectionJob.cs` — `InCurrentBillingCurrency`
- EDIT `backend/src/FinanceSentry.Modules.Subscriptions/Domain/DetectedSubscription.cs` — `UpdateFromDetection` restates `Currency`
- EDIT `backend/src/FinanceSentry.Modules.Subscriptions/Application/Services/SubscriptionDetectionResultService.cs`
- EDIT `backend/tests/FinanceSentry.Tests.Unit/BankSync/Application/Subscriptions/SubscriptionDetectionAlgorithmTests.cs`
- EDIT `backend/tests/FinanceSentry.Tests.Unit/BankSync/Infrastructure/PriceHikeSentinelPipelineTests.cs`

## [US2] Duplicate charge — files touched
- EDIT `backend/src/FinanceSentry.Modules.Alerts/Domain/AlertType.cs` — add `DuplicateCharge`
- EDIT `backend/src/FinanceSentry.Core/Interfaces/IAlertGeneratorService.cs` — add method
- EDIT `backend/src/FinanceSentry.Modules.Alerts/Application/Services/AlertGeneratorService.cs` — implement
- EDIT `backend/src/FinanceSentry.Modules.Companion/Domain/CompanionEventKind.cs` — add `DuplicateCharge`
- EDIT `backend/src/FinanceSentry.Modules.Companion/Application/Services/MaterialityPolicy.cs` — map it
- NEW `backend/src/FinanceSentry.Modules.BankSync/Infrastructure/Jobs/DuplicateChargeDetectionJob.cs`
- EDIT `backend/src/FinanceSentry.Modules.BankSync/BankSyncModule.cs` — register + schedule daily
- NEW `backend/tests/FinanceSentry.Tests.Unit/BankSync/Infrastructure/DuplicateChargeDetectionJobTests.cs`

## [US3] Category spike — files touched
- EDIT `backend/src/FinanceSentry.Modules.Alerts/Domain/AlertType.cs` — add `CategorySpike`
- EDIT `backend/src/FinanceSentry.Core/Interfaces/IAlertGeneratorService.cs` — add method
- EDIT `backend/src/FinanceSentry.Modules.Alerts/Application/Services/AlertGeneratorService.cs` — implement
- EDIT `backend/src/FinanceSentry.Modules.Companion/Domain/CompanionEventKind.cs` — add `CategorySpike`
- EDIT `backend/src/FinanceSentry.Modules.Companion/Application/Services/MaterialityPolicy.cs` — map it
- NEW `backend/src/FinanceSentry.Modules.BankSync/Infrastructure/Jobs/CategorySpikeDetectionJob.cs`
- EDIT `backend/src/FinanceSentry.Modules.BankSync/BankSyncModule.cs` — register + schedule daily
- NEW `backend/tests/FinanceSentry.Tests.Unit/BankSync/Infrastructure/CategorySpikeDetectionJobTests.cs`

## [US4] FX spread — files touched
- EDIT `backend/src/FinanceSentry.Modules.Alerts/Domain/AlertType.cs` — add `FxSpread`
- EDIT `backend/src/FinanceSentry.Core/Interfaces/IAlertGeneratorService.cs` — add method
- EDIT `backend/src/FinanceSentry.Modules.Alerts/Application/Services/AlertGeneratorService.cs` — implement
- EDIT `backend/src/FinanceSentry.Modules.Companion/Domain/CompanionEventKind.cs` — add `FxSpread`
- EDIT `backend/src/FinanceSentry.Modules.Companion/Application/Services/MaterialityPolicy.cs` — map it
- NEW `backend/src/FinanceSentry.Modules.BankSync/Infrastructure/Jobs/FxSpreadDetectionJob.cs`
- EDIT `backend/src/FinanceSentry.Modules.BankSync/BankSyncModule.cs` — register + schedule daily
- NEW `backend/tests/FinanceSentry.Tests.Unit/BankSync/Infrastructure/FxSpreadDetectionJobTests.cs`

### Rate-freshness follow-up (the reference rate has to be one somebody published)
- EDIT `backend/src/FinanceSentry.Core/Utils/CurrencyConverter.cs` — `RatesUpdatedAtUtc` + `AreRatesFresh`
- EDIT `backend/src/FinanceSentry.Modules.BankSync/Infrastructure/Jobs/FxSpreadDetectionJob.cs` — stand down on stale rates
- EDIT `backend/src/FinanceSentry.API/appsettings.json` — declare the `HygieneSentinels` block
- EDIT `backend/tests/FinanceSentry.Tests.Unit/Fx/CurrencyConverterTests.cs`
- EDIT `backend/tests/FinanceSentry.Tests.Unit/BankSync/Infrastructure/FxSpreadDetectionJobTests.cs`

`CurrencyConverter` seeds itself with hardcoded constants (EUR 1.08, GBP 1.27, UAH 0.024). They are
the live table until `ExchangeRateRefreshJob` first ticks, and they survive any feed outage after
that — `RunAsync` deliberately keeps the current table when the provider yields nothing, and
nothing downstream could tell. Every other consumer only *normalises magnitudes* with those rates,
which degrades to an approximate total. The FX-spread sentinel is the sole consumer that judges one
rate **against** another: its entire measurement is the gap between the bank's implied rate and the
reference, so a drifted reference manufactures a gap no bank charged. At the seed's EUR 1.08 /
UAH 0.024 against a real ≈1.16 / ≈0.0206, a fair UAH→EUR conversion computes a ~7% "spread" — over
twice the 3% threshold, i.e. a confident accusation on every honest conversion.

The sentinel therefore stands down when the table was never refreshed or has aged past
`HygieneSentinels:FxSpreadMaxRateAgeHours` (default 48 — the refresh is daily, so one missed run is
tolerated). The next daily tick re-examines the same 3-day lookback window, so an outage shorter
than that defers conversions rather than dropping them; an outage past `MaxRateAge + lookback` does
lose the ones that age out meanwhile, which is the accepted trade against alerting on fiction — the
skip is logged each tick so the outage is visible. `UpdateRates` ignores a null/empty feed
*including* the freshness stamp, so an outage cannot masquerade as a refresh. A non-positive
`MaxRateAgeHours` is never fresh, which is also the sentinel's off switch.

Not in this slice: the sentinel filters `t.IsActive` but not `t.IsPending`, and a pending leg
coexists with its posted twin as a separate active row. The two can pair with different credits and
alert twice for one conversion, since the dedup key is the debit transaction id. Fixing it means
deciding what `TransferDetectionService` should do with pending legs, which cash-flow also depends
on — its own change.

## Constraints
- DetectedSubscription.UserId is `string`; BankAccount.UserId is `Guid` — convert at the adapter boundary with `Guid.Parse(s.UserId)`
- Amounts summed or ranked across currencies go through `CurrencyConverter.ToUsd` first. Amounts compared to each other at a tolerance finer than the rate table's drift (the price-hike clustering and threshold) are not converted — they are partitioned by currency instead, so nothing is compared across units at all
- Spend is selected by direction, never by sign: adapters persist a positive `Transaction.Amount` with `TransactionType` = `"debit"`/`"credit"`, and every persist path runs `Transaction.ValidateInvariants`, which rejects a negative amount. The 044 sentinels that filter for outflows (`DuplicateChargeDetectionJob`, `CategorySpikeDetectionJob`) use `(t.Amount < 0 || t.TransactionType == "debit")` and exclude `IsPending` — pending and posted rows coexist, so counting both doubles a month's spend. The `Amount < 0` arm is defensive; no ingest path can produce such a row. Pre-existing `UnusualSpendDetectionJob` still filters on `t.Amount < 0` alone and is therefore inert — out of scope here, tracked as follow-up
- BankSync job can inject `ISubscriptionHygieneSummaryReader` without a project reference to Subscriptions — DI resolves at runtime via the composition root
- `CurrencyConverter`'s table is only guaranteed *approximate*: unknown currencies fall back 1:1 and known ones fall back to a hardcoded seed. Normalising a total may rely on it; comparing one rate to another may not — gate on `AreRatesFresh` first
