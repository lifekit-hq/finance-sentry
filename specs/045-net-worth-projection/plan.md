# Implementation Plan: Net Worth Projection from Savings Contributions

**Branch**: `045-net-worth-projection` | **Date**: 2026-09-02 | **Spec**: [spec.md](./spec.md)

## Architecture Decisions

- **Arithmetic lives in `dashboard.computed.ts`, not the component.** Every other dashboard
  derivation (pace deltas, savings-rate chips, chart series) is a computed signal over
  `completeMonths()`; the projection is the same shape of derivation over the same window, so
  reusing that window is what guarantees FR-007's "never the in-progress month" for free.
- **Selected rate is store state (`projectionReturnRate`), not component-local.** Precedent:
  `historyRange` and `netWorthStacked` both sit in `DashboardState`. Components on this repo
  hold no state (`.claude/rules/signal-store.md`: "no local `isLoading`/`errorMessage` fields"), and the rate has
  to be readable from `dashboard.computed.ts` to feed the projection.
- **Rate is deliberately NOT synced to the URL.** `historyRange` is, because it changes what is
  fetched and a deep link should reproduce the data. The rate changes only an assumption
  applied to already-loaded data, and persisting a speculative return in a shareable URL would
  make an assumption look like a finding.
- **Contributions do not compound.** Only the existing market-marked balance does. The app has
  no idea whether next month's savings land in a brokerage or in a current account, so
  compounding them would invent an allocation decision the user never made.
- **The market-marked base comes from the latest *valid* snapshot** (the existing
  `validHistory()` filter, which drops zero-total days where a feed was missing). Snapshots
  only start 2026-06-30, so "no snapshot at all" is a live case, not a theoretical one — hence
  FR-005's explicit wording rather than a silent zero.

## Story Slice [US1] — Projection tile + market-return toggle

One coherent slice: the tile is meaningless without a rate assumption, and the toggle has
nothing to modify without the tile. Issue #539 sizes the whole thing at S / 1 PR.

### Files touched

- `frontend/src/app/modules/bank-sync/store/dashboard/dashboard.state.ts` — add
  `projectionReturnRate: number` (default `0`)
- `frontend/src/app/modules/bank-sync/store/dashboard/dashboard.methods.ts` — add
  `setProjectionReturnRate(rate: number)`
- `frontend/src/app/modules/bank-sync/store/dashboard/dashboard.computed.ts` — median helper,
  `hasProjection`, `projectedNetWorthFormatted`, `medianMonthlySavingsFormatted`,
  `projectionBasisLabel` (carries the sample size), `projectionAssumptionLabel`
- `frontend/src/app/modules/bank-sync/constants/dashboard/dashboard.constants.ts` —
  `PROJECTION_RETURN_RATES` (the toggle's options)
- `frontend/src/app/modules/bank-sync/pages/dashboard/dashboard.component.ts` — the tile,
  gated on `store.hasProjection()`
- `frontend/src/app/modules/bank-sync/store/dashboard/dashboard.computed.spec.ts` — median
  selection, the <3-month gate, the 0% path, a non-zero-return path
- `frontend/e2e/net-worth-projection.spec.ts` — Playwright: tile renders with 3+ complete
  months, hidden below 3, rate toggle changes the figure without refetching

### Constraints discovered

- `HISTORY_RANGE_MONTHS['3m'] = 3` and the backend's `MonthWindow.StartOfMonthsAgo(3)` floors to
  a month boundary *then* subtracts, so the payload carries 3 complete months **plus** the
  in-progress one. The default 3M range therefore clears the ≥3 gate exactly — the tile is not
  dead on first load.
- `NetWorthSnapshotDto.totalNetWorth` is the snapshot's total, but the tile's starting point is
  `DashboardData.totalNetWorthUsd` (live, not snapshot-dated). The sleeve split for the return
  assumption necessarily comes from the snapshot, so the two figures can disagree by a day's
  drift; the assumption wording says "market-marked sleeves" rather than restating a total.
- Sandbox: `NODE_AUTH_TOKEN` is unset, so `npm ci` 401s on `@lifekit-hq/*`. Build the library
  from the lifekit-common source clone and install tarballs per AGENTS.md before running the
  Angular build + Playwright.

### Verification

`npx eslint` on changed files, `npx ng test --watch=false` (Vitest), `npx ng build`, and
`npx playwright test --reporter=json` for the app-surface UI gate.

## Story Slice [US2] — the projection shows its addends

Decided after US1 shipped: the US1 tile blends contributions and assumed return into one
number, which contradicts the separation the feature is named for. This slice is display-layer
only — it adds no new arithmetic, it *exposes* the two quantities `dashboard.computed.ts`
already computes internally (`medianMonthlySavings * PROJECTION_HORIZON_MONTHS` and
`marketGrowth`), which is why it is a separate reviewable slice rather than a rewrite.

### Files touched

- `frontend/src/app/modules/bank-sync/store/dashboard/dashboard.computed.ts` — a module-level
  `signedUsd` formatter plus three exposed computeds: `projectionTodayFormatted`,
  `projectedContributionsFormatted`, `projectedMarketReturnFormatted`
- `frontend/src/app/modules/bank-sync/pages/dashboard/dashboard.component.ts` — the addend rows
  inside the existing projection `cmn-card`
- `frontend/src/app/modules/bank-sync/store/dashboard/dashboard.computed.spec.ts` — the addends
  sum to the headline at 0% and at a non-zero rate; the rate moves only the return addend; a
  negative median renders a signed contributions line
- `frontend/e2e/net-worth-projection.spec.ts` — the addend rows render, and selecting 5% moves
  the market-return row off `$0` while the contributions row holds

### Constraints discovered

- The tile's money figures use `currency.transform(..., '1.0-0')` (full form, `$22,000`), not
  the `COMPACT_FORMATTER` (`$22K`) the stat cards use. The addends must match the headline's
  formatter or the column will not visibly sum.
- `netWorthChangeFormatted` already establishes the signed-money convention in this file:
  U+2212 MINUS SIGN for negatives, ASCII `+` for positives. `signedUsd` follows it rather than
  inventing a second convention two hundred lines away.
- Exact zero is special-cased to a bare `$0`. A market return of `+$0` reads as a rounding
  artifact; `$0` reads as the deliberate flat default, which is the point of the 0% option.
