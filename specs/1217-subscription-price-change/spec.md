# 1217 — Subscription price-change alert

Issue #1217. Builds on subscription detection (014) and feeds the Alerts tier (012). Supersedes 044 US1 (the daily
price-hike sentinel).

## Problem
A subscription that quietly goes up (EUR 9.99 → EUR 12.99) is only noticed at month-end, if at all. 044 US1 compared the
latest charge to a baseline once a day against a 15% threshold, so a smaller rise never alerted, nothing recorded *that*
a price changed, and its alert was auto-resolved when the comparison stopped holding.

## Solution
Detection already sees every charge of a detected subscription. When a new charge of an existing detected subscription
differs from that subscription's previous charge by more than a small tolerance, the change is stored as a
`SubscriptionPriceChange` event. An increase raises one `PriceHike` alert; a decrease is only recorded.

## Acceptance
- A new charge (later `LastChargeDate`) on an existing, detection-owned subscription, in the same currency as its previous
  charge, whose amount differs from the previous charge by more than 2% (`SubscriptionPriceChange.Tolerance`: rounding),
  records a price-change event: old amount, new amount, change in percent, charge date.
- An increase raises a `PriceHike` alert naming the merchant and the old and new amounts; it opens the Subscriptions page
  (`AlertAppPath`) and references the subscription. A decrease records the event and raises nothing.
- One alert per actual change: the event exists only when the stored last charge amount moves, so a later charge at the
  new price compares equal and records nothing. A later, further change raises its own alert and supersedes the still-open
  earlier one (one open alert per user, type and reference).
- A charge in another currency than the previous one is not comparable (a card moved between accounts restates the same
  price): no event, and the row restates its unit as before.
- The subscription list returns each subscription's latest price change and the Subscriptions page shows it.
- Price changes are part of the person's data export and erasure.

## Non-goals
- Predicting increases or comparing against market data; cancelling or negotiating subscriptions.
- New detection logic: only what 014 already detects, and only detection-owned rows (a manual commitment's amount is the
  user's own figure).
- A change that happened before the subscription was first detected: there is no earlier charge to compare with.
- New channels: the alert rides the existing Alerts tier, web push and companion event (`CompanionEventKind.PriceHike`).

## Decisions
- **Replaces the 044 US1 sentinel.** Two mechanisms for one event would alert twice for a rise over 15%, and the
  sentinel's resolve-when-below-threshold step would close the new alert of a small rise the next day. The daily job,
  `ISubscriptionHygieneSummaryReader`, `HygieneSentinels:PriceHikeThreshold` and `IAlertGeneratorService.ResolvePriceHikeAlertAsync`
  are removed; `detected_subscriptions.PreviousAmount` stays (detection still needs the displaced cluster to keep a
  repriced row listed).
- **Event, not state.** The alert is raised where the change is observed (`SubscriptionDetectionResultService`), with
  `AlertDedup.Always` + `SupersedeOpen`: the event itself is the dedup.
