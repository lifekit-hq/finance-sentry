# 1217 — Subscription price-change alert: Tasks

- [x] `SubscriptionPriceChange` entity, DbContext mapping, migration M007, repository methods
- [x] `UpdateFromDetection` returns the change; result service stages it and alerts on an increase
- [x] `PriceHike` alert worded as old → new, `Always` + `SupersedeOpen`
- [x] Retire the 044 US1 daily sentinel (job, reader, option, resolve method, tests) and fix 044 docs
- [x] Export/erase coverage for the new table
- [x] `latestPriceChange` on `SubscriptionDto` and the Subscriptions page
- [x] Tests: increase, decrease, within tolerance, repeat at the new price, other currency, supersede, query, UI
