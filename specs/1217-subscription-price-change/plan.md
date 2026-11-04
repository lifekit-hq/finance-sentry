# 1217 — Subscription price-change alert: Plan

## Backend (Subscriptions module)
- `Domain/SubscriptionPriceChange` — event row (`subscription_price_changes`): `UserId` (string, as `DetectedSubscription`),
  `SubscriptionId`, `OldAmount`, `NewAmount`, `Currency`, `ChangePercent`, `ChargeDate`, `DetectedAt`. `Tolerance` (2%) and
  `TryDetect` live here.
- `DetectedSubscription.UpdateFromDetection` returns the change it implies (it holds the before and after state); the
  caller stores it and alerts on an increase.
- `SubscriptionDetectionResultService` stages the change with the row update (one `SaveChanges`) and emits the alert
  through `IAlertGeneratorService`; an alert failure is logged, never fails detection.
- Migration `M007_AddSubscriptionPriceChanges`; Owner query filter on the new set; exporter and eraser cover it.
- `GetSubscriptionsQuery` joins each row's latest change into `SubscriptionDto.LatestPriceChange`.

## Alerts / BankSync
- `GeneratePriceHikeAlertAsync(old, new)` words the alert as a change and uses `Dedup.Always` + `SupersedeOpen`.
- Removed: `PriceHikeDetectionJob` (+ Hangfire entry, removed from storage with `RemoveIfExists`),
  `ISubscriptionHygieneSummaryReader`, `SubscriptionHygieneSummaryReader`, `PriceHikeThreshold`, `ResolvePriceHikeAlertAsync`.

## Frontend
- `Subscription.latestPriceChange` and a muted line on the subscription row ("Price up 9.99 → 12.99 (+30%) on 3 Nov").
