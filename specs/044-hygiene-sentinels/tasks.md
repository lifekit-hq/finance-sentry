# 044 — Hygiene Sentinels: Tasks

## [US1] Price hike detection

- [x] Create `ISubscriptionHygieneSummaryReader` port in Core
- [x] Implement `SubscriptionHygieneSummaryReader` in Subscriptions module + register
- [x] Add `PriceHike` to `AlertType`, `IAlertGeneratorService`, `AlertGeneratorService`
- [x] Add `PriceHike` to `CompanionEventKind` + `MaterialityPolicy`
- [x] Implement `PriceHikeDetectionJob` + register + schedule in BankSyncModule
- [x] Write unit tests for `PriceHikeDetectionJob`
- [x] Carry the pre-step price through detection so a real hike can reach the sentinel — the 15% cluster tolerance and the 15% threshold had made every firing series undetectable
- [x] Persist `PreviousAmount` on `detected_subscriptions` (M006) and surface it on `SubscriptionHygieneSummary`
- [x] Measure the hike against `HikeBaseline`, not the average that already contains the raised charge
- [x] Write an end-to-end test running a charge series through the real detect → persist → read → alert chain
- [x] Price the merchant in one currency — a card moved between accounts restated the same charge as a 20% hike
- [x] Cover the restatement's money consequence — the stored `Currency` is the unit the spend summary runs `ToUsd` over, so a restated row has to convert at the new rate

## [US2] Duplicate charge detection

- [x] Add `DuplicateCharge` to `AlertType`, `IAlertGeneratorService`, `AlertGeneratorService`
- [x] Add `DuplicateCharge` to `CompanionEventKind` + `MaterialityPolicy`
- [x] Implement `DuplicateChargeDetectionJob` + register + schedule in BankSyncModule
- [x] Write unit tests for `DuplicateChargeDetectionJob`

## [US3] Category spike detection

- [x] Add `CategorySpike` to `AlertType`, `IAlertGeneratorService`, `AlertGeneratorService`
- [x] Add `CategorySpike` to `CompanionEventKind` + `MaterialityPolicy`
- [x] Implement `CategorySpikeDetectionJob` + register + schedule in BankSyncModule
- [x] Write unit tests for `CategorySpikeDetectionJob`
- [x] Select debits by direction, not sign — `t.Amount < 0` matched nothing in production
- [x] Exclude pending rows so a charge is not counted twice alongside its posted twin

## [US4] FX spread detection

- [x] Add `FxSpread` to `AlertType`, `IAlertGeneratorService`, `AlertGeneratorService`
- [x] Add `FxSpread` to `CompanionEventKind` + `MaterialityPolicy`
- [x] Implement `FxSpreadDetectionJob` + register + schedule in BankSyncModule
- [x] Write unit tests for `FxSpreadDetectionJob`
- [x] Give `CurrencyConverter` a freshness stamp an outage cannot forge (`RatesUpdatedAtUtc` / `AreRatesFresh`)
- [x] Stand the sentinel down on stale rates — a spread measured against the offline seed accuses honest conversions
- [x] Pin the sentinel's tests to a refreshed table, not the seed, so reading the seed fails them
- [x] Declare the `HygieneSentinels` block in appsettings so every threshold is discoverable

## [US5] Sentinel hardening (review pass over US1–US4)

- [x] Delete the inert `UnusualSpendDetectionJob` and withdraw its deployed Hangfire schedule — `CategorySpikeDetectionJob` supersedes it
- [x] Collapse the 17 hand-rolled dedup blocks in `AlertGeneratorService` into one `EmitAsync` plus a silence-window table
- [x] Pin the dedup discipline for `PriceHike`/`DuplicateCharge`/`CategorySpike`/`FxSpread` — active-alert gate and silence window, one pair each
- [x] Assert every live `AlertType` declares a silence window, so a new type cannot fail first inside a background job
- [x] Name the merchant in a duplicate-charge alert the way the statement did, keeping the normalized key as the dedup anchor
- [x] Skip the unnameable-merchant group — every blank merchant normalizes to one key, so unrelated charges sharing an amount read as a duplicate
