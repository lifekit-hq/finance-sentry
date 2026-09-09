# 044 — Hygiene Sentinels

## Problem
Finance Sentry holds transaction history, detected subscriptions, and multi-currency account data, but raises no alerts when recurring charges silently creep up, the same charge appears twice in a short window, a spend category explodes past historical norms, or a cross-currency round-trip bleeds excessive FX spread. These patterns are easy to miss until month-end.

## Solution
Four deterministic detectors — one Hangfire job per detector, all emitting through the existing Alerts plumbing (012) with the dedup discipline from issue #419. Thresholds configurable via appsettings; zero LLM calls on the tick path.

## User Stories

### [US1] Price hike detection (P1)
Recurring merchant amount drifts up — subscription or installment creep.

**Acceptance**:
- Fires when `LastKnownAmount > HikeBaseline × (1 + threshold)` and `OccurrenceCount ≥ 3`, where `HikeBaseline` is `PreviousAmount ?? AverageAmount` — the price billed before the most recent step when detection recorded one, otherwise the running average. Measuring against the average alone can never fire: the charge that raised the price is itself in the average diluting it (see plan.md, "The hike baseline")
- Reads `detected_subscriptions` (014) — recurrence not re-derived
- Threshold: `HygieneSentinels:PriceHikeThreshold` (default 15 %)
- Alert type: `PriceHike`; active-alert gate + 30-day silence per subscription id
- Companion delivery via `CompanionEventKind.PriceHike`

### [US2] Duplicate charge detection (P1)
Same merchant charges same amount twice within N days on one account.

**Acceptance**:
- Fires when ≥ 2 active non-pending debit transactions share `(AccountId, normalized merchant, Amount)` within `HygieneSentinels:DuplicateWindowDays` (default 5)
- Alert type: `DuplicateCharge`; 7-day silence per `(AccountId, merchant, Amount)`
- Companion delivery via `CompanionEventKind.DuplicateCharge`

### [US3] Category spike detection (P2)
Month-to-date spend in a category meaningfully exceeds 6-month baseline.

**Acceptance**:
- Baseline: average monthly spend over the past 6 complete months — total category spend divided by the months the user was *observed* for (first transaction onward, capped at 6), not by the months that happen to hold rows for the category. A month inside the observed span with no spend in the category is the zero it is; a month before the user's first transaction is no data and is not averaged in. All amounts in USD via `CurrencyConverter.ToUsd`
- Requires ≥ 4 months of history; fires when current-month > baseline × `HygieneSentinels:CategorySpikeMultiplier` (default 2.0)
- Alert type: `CategorySpike`; 7-day silence per `(UserId, category)`
- Companion delivery via `CompanionEventKind.CategorySpike`

### [US4] FX spread detection (P2)
A cross-currency round-trip between own accounts loses more than X% to conversion.

**Acceptance**:
- Matches cross-currency transfer pairs within `HygieneSentinels:FxSpreadLookbackDays` (default 3) using BankSync's existing cross-currency matching logic. Only settled legs participate — a hold's amount is provisional, and a hold that settles at a different amount is never retired, so it would alert a second time on its own
- Computes implicit rate from pair amounts; compares to `CurrencyConverter` market rate
- Fires when spread > `HygieneSentinels:FxSpreadThreshold` (default 3 %)
- Alert type: `FxSpread`; 7-day silence per `(UserId, debit transaction id)`
- Companion delivery via `CompanionEventKind.FxSpread`

## Global acceptance criteria
- All four detectors work off data FS already holds — no new external feed
- Price-hike reads `detected_subscriptions`, not re-derived recurrence
- Every detector emits through Alerts (012) with dedup per #419 discipline
- Detection is fully deterministic — zero LLM calls on the tick path
- All thresholds configurable via appsettings, not hardcoded at the call site
- Repeat firing dedupes instead of re-alerting on each run
