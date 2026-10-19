# Finance Framework — the rules for any number

The rule set for how Finance Sentry computes and presents financial truth: one vocabulary, one set of
measures, one headline contract, and one contract every source of wealth plugs in through.

- **This document vs `money-semantics.md`.** [money-semantics.md](money-semantics.md) records what each
  *existing* number means today, per provider. This document is the rule set for *any* number, existing or
  new. Where the two disagree about today's behaviour, money-semantics and the code win, and this document
  is stale: fix it.
- **Running source of truth.** A PR that changes money math updates **both** documents in the same diff
  (or says in its description why one is untouched). Decided choices move between the *Decided* and *Open*
  lists below when the captain decides; known gaps are removed when the code closes them.
- **Provenance.** Adapted from the finance-framework scout report (2026-10-14, kept in the operator's
  vault; "report §n" below). Evidence appendices (code reference tables, open-source clones, source quotes)
  stay in the report and are not copied here. Every number in this document is a fictional example.
- **Code references** name files and symbols, not line numbers, because lines drift.

Status words used below: **built** (in the code and behaving as described), **partial**, **designed** (a
rule or shape agreed but not implemented), **missing**.

---

## 1. Decided and open

### Decided (captain's review of the framework report, October 2026)

| Id | Decision | What it fixes |
|---|---|---|
| C3 | The book headline is the **cash-inclusive brokerage**, with "invested only" shown beneath. The benchmark is **SPY**. | Scope of the book figure and of M3/M9. Book vs SPY's TWR and the contributions vs growth split run on the cash-inclusive brokerage (C2(a)); the `book` value-change headline stays invested-only. |
| C4 | **Compute in USD; show returns in the person's base currency**, using dated rates for flows and values. | Currency of M3–M5. Flows convert at the flow date's rate, positions at the valuation date's rate (§3.2 rule 5). |
| C5 | The net-worth headline is the **total**, with "of which liquid" beneath. No toggle. | §5.3 totals; presentation pattern P10. |
| C7(i) | **Designs only** from AGPL/GPL projects; **code only** from permissively licensed ones, with attribution. | What may be ported. AGPL/GPL code is never copied; AGPL designs are paraphrased. |
| C8 | **One shared, disclosed projection assumption set**, shown in words next to every projection, as a **range**. | M10; presentation pattern P5. Today's two projections use two sets (gap G7). |
| C2(a) | **Contributions vs growth for IBKR and Inzhur now, showing both MWR and TWR** (2026-10-15): "Contributed / Growth" with a Modified Dietz return on the Home Investments card, and Book vs SPY as a time-weighted return. MWR only was declined. | M2 and M4 on the brokerage sleeve (§3.4); M3 and M9 on Book vs SPY (G1 closed). Internal moves are not flows; dividends and interest count as growth unless withdrawn. |
| C9 | **Per-holding checks first**, each threshold cited from a public source. No portfolio-level score. | Presentation pattern P7; the per-holding screen of #824. |
| C6 | **Steam is not linked; the person enters it by hand** (2026-10-18): no wallet, inventory or API integration. The entry is valued at what they would receive after fees, L3, IN_ECONOMY. | The manual source (§5.4, §5.5): counted in total net worth, outside cash and liquid net worth, and the total ESTIMATED (`ESTIMATED_VALUE`). Bank-side Steam charges stay spending; the entry joins as a newly linked line (§3.2 rule 3), so nothing is counted twice. |

### Open

| Id | Question | State |
|---|---|---|
| C1 | IBKR prerequisite steps: enabling Deposits & Withdrawals (and optionally Change in NAV) in the Flex query, and the one-off backfill. | Open, with the provider-data decisions. Without it no IBKR external flow arrives (gap G6). |
| C2 | Contributions vs growth: (b) crypto flows now or later; (c) moving `ibkr-flex` out of shadow alerting. (a) is decided above. | (c) decided, captain 2026-10-15: `ibkr-flex` is in `ActiveProviders`. The rest is open, with the provider-data decisions. (b) started: S2a reads the venue fiat legs and Revolut X transfers, S2b Binance's coin deposits and withdrawals and matches transfers between the venues, S2c the earn, airdrop and dust events (G6). |
| C7(ii) | Whether the public repository, unlicensed today, gets a license, and which. | Open; the captain's call. Without a license, default copyright applies. |

---

## 2. Architecture

### 2.1 The umbrella

```
 Layer 2: economies, one adapter each (section 5)
 ┌────────────┬────────────┬────────────┬────────────┬──────────────┬──────────┐
 │ Banks      │ Brokerage  │ Crypto     │ Funds      │ Marketplaces │ Manual   │
 │ Monobank   │ IBKR       │ Binance    │ Inzhur     │ (not linked, │ entries  │
 │ TrueLayer  │            │ Revolut X  │            │ C6)          │ (Steam)  │
 └─────┬──────┴─────┬──────┴─────┬──────┴─────┬──────┴──────┬───────┴────┬─────┘
       └────────────┴──── one SourceSnapshot per source per day ──────────┘
            positions · cash · liabilities · dated flows · prices · capabilities
                                      │
                 Normalization: the five-step valuation rule (5.3)
       principal market → exit price → reliability L1-L3 → convertibility →
       dated FX to the base currency (or UNAVAILABLE, never a silent 1:1)
                                      │
             Snapshots: net_worth_snapshots + one row per source (5.6)
                                      │
              Measure engine, pure, in FinanceSentry.Core (layer 1, 3.4)
     M1 change · M2 contributions vs growth · M3 TWR · M4 MWR · M5 bridge ·
     M6 gain vs cost · M7 allocation · M8 risk · M9 benchmark · M10 projection
                                      │
            Headline records: amount + percent + method + quality (3.6)
                                      │
       Presentation: dashboard · accounts · investments · MCP tools · briefs
```

This is the target shape. Today the top (three sources registered as `IWealthSource`, R8 PR 1, a
per-source row written beside every snapshot, R8 PR 2, and read back by history, the dashboard, MCP and FIRE,
R8 PR 3) and bottom (dashboard, MCP, briefs) are wired. The measure engine is built for M1-M6, M8 and M9 (3.4); the
headline record (3.6) is built in the backend and MCP and calls M1 for the figures that are changes. The stored
total is the sum of every registered source's resolved figure; the three sleeve columns are frozen by
R8 PR 4: kept for history, never extended (5.6).

Three rules keep the layers apart:

1. **Adapters know their economy and nothing else.** They report what is held, what moved and what each
   thing is worth. They never compute a return.
2. **The measure engine knows nothing about providers.** It receives values, dated flows and capability
   flags, and is pure: no I/O, no clock. `BookDrawdownCalculator` (`FinanceSentry.Modules.Risk`) is already
   built this way and is the template.
3. **Presentation formats; it does not decide meaning.** Whether a percentage is meaningful, what method it
   used and how trustworthy it is travel with the figure. Today the dashboard derives its own percentage in
   `dashboard.computed.ts`, so nothing can tell it that the percentage is misleading (gap G3).

### 2.2 Where the code is today

| # | Figure | Where | Method today | Quality signal today | Measure | Gap |
|---|---|---|---|---|---|---|
| 1 | Net worth total | `NetWorthSnapshot` (three sleeve columns plus total); live total from `BookFiguresService` (sums the registered `IWealthSource`s: banking, brokerage, crypto) | Sum of every registered source's figure in USD through `CurrencyConverter.ToUsd` at today's rates | `StaleSleeves`, `IsApproximate`, a null `CashTotal`; `FxStatus` (R4): `BookFigures.FxStatus` on the live total, `fx_status` on a stored row (null on a row from before the column) | valuation, M7 | G2 (live total closed), G4, G5 |
| 2 | Dashboard "Change" and its % | `dashboard.computed.ts`; label in `dashboard.component.ts` | (end − start) / start over snapshots: deposits, savings and newly linked accounts all count | the `changeHeadline` badge (P9); the percentage itself is still not gated | M1, later M5 | G3 |
| 3 | (retired) Dashboard "Invested change" | removed from Home; the Investments card's growth figure replaces it | — | — | — | none |
| 4 | Book vs SPY (weekly brief, MCP `get_book_performance`, `GET /radar/book-performance` behind the Home Investments card's growth of 10,000) | `BookPerformanceService` | Since C2(a): daily-chained TWR on the cash-inclusive brokerage total with the brokers' deposits and withdrawals taken out and provider links bridged (`ScopeLinks`); SPY's adjusted-close return over the same dates, `delta` = the difference. The `book` headline stays the change in invested value. Beside it, since R1(b), the flow-neutral **holdings-based return** (`holdingsBasedReturn`) against SPY's price return (`spyPriceReturn`, close) | null when history misses the edge tolerance; `bookTwrReasons` (`UNKNOWN_FLOW` while a provider has no deposits on record, `SCOPE_CHANGED` on a link) and `bookTwrQuality`, the worst quality they imply; the holdings-based figure carries a typed reason instead of a number | M3, M9; `book` is M1 | none (G1 closed) |
| 5 | Book drawdown | `BookDrawdownCalculator` | Flow-neutral chained index from holdings snapshots, with split detection | null under two captures | M8 | none: the pattern to reuse |
| 6 | Position returns | `PositionReturnCalculator` | Average-cost walk: unrealized, realized, dividends net of withholding, currency effect | `Coverage` = complete / partial / unavailable | M6 | the vocabulary is local (G5) |
| 7 | 12-month projection tile | `dashboard.computed.ts` | Median monthly savings × 12, no market return | hidden under three complete months | M10 | G7 |
| 4b | Contributed vs growth (Home Investments card, `GET /wealth/brokerage/growth` and `GET /wealth/crypto/growth`, one card each) | `GetSleeveGrowthQuery` | growth = end − start − newly linked − net deposits on the sleeve total since the value series starts (brokerage: cash-inclusive; crypto: the venues' `crypto_total`); Modified Dietz on the same window, and beside it `timeWeightedReturn` (M3 over the same closes and flows; null while the split is withheld or value moved with no recorded flow) | UNAVAILABLE with `UNKNOWN_FLOW` while a provider has no deposits on record or a venue's walk is behind; `SCOPE_CHANGED` on a link; `UNMATCHED_TRANSFER` (crypto) for a transfer leg no other venue's leg matched | M2, M3, M4 | IBKR flows wait on C1; crypto earn, dust, airdrops and fees wait on S2c |
| 8 | FIRE projection | `FireCalculator`; money-semantics §11 | Target = 12 × median outflow ÷ withdrawal rate (default 4%); 5% real return, compounding | `HasStaleSleeves`, `InsufficientHistory` | M10 | G7 |
| 9 | Historical FX | `CachingHistoricalExchangeRateService` | `ConvertToUsdAsync` returns a `ConvertResult`: the published rate for the date (`EXACT`), the closest earlier published day (`NEAREST_PUBLISHED`), else today's rate (`TODAY_RATE`, or `SEED_TABLE` / `UNKNOWN_1TO1` when today's rate is itself the seed or absent) | `FxStatus` on the result (R4); the log line stays | valuation step 5 | G2 (closed for the live total) |

Two things already right and kept:

- **The compute currency is USD and the display base is per user** (`CurrencyConverter.ResolveBase`, used
  by the dashboard, net-worth history and wealth controllers).
- **`IHistoricalExchangeRateService.GetPublishedRateAsync` never substitutes today's live rate.** It is the
  primitive the exact FX status is built on.
- **Typed FX status (R4, done).** `CurrencyConverter.Convert` and `IHistoricalExchangeRateService.ConvertToUsdAsync`
  return `ConvertResult(Amount, FxStatus)`; `FxStatus` is `EXACT`, `NEAREST_PUBLISHED`, `TODAY_RATE`,
  `SEED_TABLE` or `UNKNOWN_1TO1`, ordered best to worst so a figure carries the worst of its conversions
  (`FxStatusExtensions.Worst`). `ToUsd` stays as the amount-only adapter and an unknown currency never throws
  (option c was rejected). The statuses map to the reasons `FX_TODAY_RATE` (today's-rate and seed-table) and
  `FX_UNKNOWN_1TO1` (`FxStatusExtensions.ToReason`). Adopted in the headline readers: the account, brokerage
  and crypto readers stamp each row, `BookFiguresService` carries the worst on `BookFigures.FxStatus`
  (`FxReasonCodes`), the snapshot job passes it in `NetWorthSnapshotData`, and net-worth history returns
  `FxReasons` per point: the job persists the worst status on the snapshot row (`fx_status`, nullable, one
  per row, zero-quantity holdings left out as the wealth sources do), history maps it (a carried-forward
  sleeve folds in the previous row's status, a backfilled row stores the worst of its accounts' with
  `TODAY_RATE` as the floor), and a row with none recorded (from before the column) falls back to
  `IsApproximate` reading `FX_TODAY_RATE`. Each per-source row carries its own FX reasons, returned per source in history's `sources` since R8 PR 3; the
  row-level `FxReasons` still reads `fx_status`. A brokerage
  or crypto row stores its USD value converted at sync time, so its status is re-derived from the rate table
  in force when it is read. Display wording is R7.
- **Dated display conversion of history (C4, done).** A non-USD base shows each net-worth history row at the
  rate published for *that row's date* (`GetPublishedSeriesAsync`: one cached lookup, one upstream request, for
  the whole window; `PublishedRateLookup.Resolve` per row), never at one rate for the whole series. A date the
  feed skipped (weekend, holiday) borrows the closest earlier published rate within
  `PublishedLookbackDays` (7: longer than any normal non-publishing run, short enough that a feed outage
  does not pass for a weekend) and carries `FX_NEAREST_PUBLISHED`; a date with nothing published inside
  that bound is shown at today's rate and carries `FX_TODAY_RATE`; an exact date carries no reason. A row dated
  today is shown at today's live rate, as the dashboard hero is. The reason a row finally carries is the worst of its stored
  reasons and the one its display conversion added (`FX_UNKNOWN_1TO1` > `FX_TODAY_RATE` > `FX_NEAREST_PUBLISHED`),
  so a base currency shown on the seed table still adds `FX_TODAY_RATE`. USD output is untouched and does no
  rate lookup. The window's `ChangeHeadline` (a `VALUE_CHANGE` figure, not a return) is read off the same
  converted rows: last visible row minus first, with the percent over the first row's base amount, so it
  always equals what the rows show (a flat USD net worth against a moving rate is a non-zero base change).
  Its FX reasons are the worse of the two end rows' conversions (no blanket `FX_TODAY_RATE`; a headline has
  no `FX_NEAREST_PUBLISHED` code, so a nearest-published end adds none to it); a percent
  withheld in USD (zero or negative start, scope change) stays withheld. Display only: stored rows keep their
  USD values, and MCP `get_net_worth_history` stays USD.
  Latency (measured 2026-10-15, cold, 2023-01-01..2026-10-14): one request, 0.6 s for Frankfurter EUR and 0.4 s for
  NBU UAH; repeats within the 12-hour cache add none. A feed that is down costs one failed request (not cached) and the rows fall back to today's rate.

---

## 3. Layer 1: the meaning layer

### 3.1 Vocabulary: one word, one meaning

| Term | Meaning in Finance Sentry | Anchor |
|---|---|---|
| **Value** | What a holding would fetch today: its exit price in its principal market, converted to the base currency at the valuation date's rate (5.3). Not cost, not the amount paid. | IFRS 13 (exit price); GIPS 2.A.19 (fair value) |
| **Net worth (total)** | Value of all assets minus all liabilities, across every source, in the base currency. | standard |
| **Liquid net worth** | The part of net worth whose convertibility is CASH (5.3 step 4). | this framework |
| **Scope** | The set of accounts a figure covers: the whole umbrella, the cash-inclusive brokerage, the invested positions only, one account. Every measure takes a scope. | Portfolio Performance `PortfolioClientFilter`; Sure `FlowClassifier` |
| **External flow** | Money or assets crossing the scope boundary by the owner's action: a deposit, withdrawal, or transfer in or out, in cash or in kind. | GIPS glossary (dividend and interest are *not* external flows) |
| **Internal transfer** | A move inside the scope: IBKR to IBKR, Binance spot to Earn, own bank to own bank. Nets to zero: never a flow, never growth. | — |
| **Income** | Dividends, interest, staking rewards, fund distributions. Part of return. | GIPS glossary; 2.A.11 |
| **Fee, tax** | Costs. Part of return, as a negative. | GIPS 2.A.13, 2.B.5 |
| **Net contributions** | External inflows minus external outflows over a window. | — |
| **Growth** | Value change minus net contributions: the investment result in money. | — |
| **Return (%)** | Always stated with its method: TWR, MWR (XIRR or Modified Dietz), gain vs cost, or holding-period return (HPR). | GIPS 1.A.35, 2.A.24, 2.A.29 |
| **Change in value** | End value minus start value, with no adjustment for flows. Includes deposits. This is what the `book` headline of `get_book_performance` is today. It is **not a return**. | — |
| **Cost basis** | What was paid for what is still held, by average cost (as `PositionReturnCalculator` does). | standard |
| **Base currency** | The person's display currency. Compute stays in USD today. | `CurrencyConverter.ResolveBase`; C4 |
| **As-of** | The timestamp of the *oldest* input in a figure: a figure is only as fresh as its stalest part. | this framework |
| **Quality** | COMPLETE, ESTIMATED, DEGRADED or UNAVAILABLE, with typed reasons (3.6). | Wealthfolio performance design (paraphrased) |

### 3.2 Flow classes

Five classes, the set Sure uses, with tax kept beside fee:

| Class | Examples | In TWR (M3) | In MWR (M4) | In contributions vs growth (M2) |
|---|---|---|---|---|
| `external_inflow` | bank → broker deposit; crypto arriving from an outside wallet; shares transferred in from another broker | splits the period, removed from the return | a dated cash flow | contribution |
| `external_outflow` | withdrawal to a bank; assets transferred out | splits the period, removed | a dated cash flow | negative contribution |
| `income` | dividend, interest, staking reward | stays inside: part of the return | stays inside | growth |
| `fee`, `tax` | commission, account fee, withholding tax | stays inside, negative | stays inside | negative growth |
| `internal` | a buy or sell inside the scope; a transfer between two accounts both inside the scope | invisible | invisible | invisible |

Providers' own type strings map onto these classes per provider, by subtype, never by type alone (report
§12.5 gives Plaid's table as the model).

Seven rules:

1. **Classification depends on scope.** The same transfer is internal for "the whole umbrella" and external
   for "IBKR only". Store the raw event and its counterparty, and classify at measure time for the scope
   asked. Never store one fixed label.
2. **Income, fees and taxes are return, never flows.**
3. **A newly linked account is not growth.** Its value existed before it was linked. At the umbrella scope
   it enters as its own bridge line, "newly linked", on the link date; an unlinked account leaves the same
   way.
4. **Unknown flows degrade the figure; they are never guessed.** If value moves in a way that neither prices
   nor recorded flows explain, the figure becomes DEGRADED with reason `UNKNOWN_FLOW`.
5. **Flows convert at the flow date's rate; positions at the valuation date's rate.** The difference is the
   FX line of the bridge. Converting flows at today's rate makes currency moves read as growth.
6. **A self-transfer between two venues nets to zero at the umbrella scope.** Match the two legs by
   transaction id or hash; otherwise by amount, asset and a gap of at most one day. An unmatched leg is
   reason `UNMATCHED_TRANSFER` until it is matched or the person confirms it.
7. **Timing within a day.** Inflows are placed at the start of the day and outflows at the end:
   r = (V_today + Out) / (V_yesterday + In) − 1 (the Portfolio Performance and Wealthfolio convention).
   Document it; the small difference from a broker's own TWR is then explainable.

### 3.3 The net-worth bridge (M5)

Every change in net worth over a window splits into named lines that add up exactly:

```
End − Start =   savings              external inflows − external outflows at the umbrella scope
                                     (the honest monthly flows of money-semantics §5)
              + investment return    market moves + investment income − fees − taxes
              + FX translation       base-currency effect of rate moves on non-base holdings
              + revaluation          re-pricing of non-market assets (a fund unit price, a manual value)
              + newly linked − unlinked
              + unexplained residual = (End − Start) − all of the above; always shown, never hidden
```

A residual above 0.2% of the start value makes the bridge DEGRADED (reason `RESIDUAL_ABOVE_TOLERANCE`).
**Status: engine built, not wired.** `NetWorthBridge` (3.4) computes these lines and the tolerance flag
from the window's classified flows plus the per-position market, FX, revaluation and linking lines;
`NetWorthBridge.Translate` splits a holding's move into its local part and its FX part. No figure shows a
bridge yet.

### 3.4 The ten measures

| Id | Measure | The question it answers | Method | Standard | Test oracle | Today |
|---|---|---|---|---|---|---|
| M1 | Value change | How much more or less do I have? | End − Start. The % is Δ ÷ Start, shown only when the scope did not change, and labelled as including deposits | — | arithmetic | dashboard "Change"; the `book` headline of `get_book_performance`. Engine: `ValueChange`, not wired |
| M2 | Contributions vs growth | How much did I put in, and how much did it earn? | growth = ΔValue − net external flows | — | Portfolio Performance categories | the brokerage sleeve's "Contributed vs growth" (`GetSleeveGrowthQuery`, flows from `BrokerageFlowReader`); the crypto sleeve's (flows from `CryptoFlowReader`: venue fiat legs, Binance coin deposits and withdrawals and Revolut X transfers, S2a/S2b; earn and staking rewards, airdrops as income and the dust service charge as a fee, S2c; withheld per venue while its walk is behind, G6) |
| M3 | Time-weighted return | How did the investments do, regardless of when I added money? The number to compare with a benchmark | Daily chain-linking (3.2 rule 7): Π(1 + r_t) − 1 | GIPS 1.A.35, 2.A.24 | Portfolio Performance `ClientIndex`; the broker's own TWR | Book vs SPY's `bookTwr` (brokerage sleeve, cash-inclusive, `BookPerformanceService`); `timeWeightedReturn` on both growth splits (`GetSleeveGrowthQuery`, the P8 timing gap) |
| M4 | Money-weighted return | What did *my money* earn, timing included? | XIRR on an actual/365 day count for windows of one year or more; Modified Dietz, not annualised, for shorter windows | GIPS 2.A.29, 2.A.12 | Excel XIRR / ExcelFinancialFunctions; hledger `roi` | Modified Dietz on both sleeves' "Contributed vs growth"; XIRR (ported from ExcelFinancialFunctions) not wired |
| M5 | Net-worth bridge | Why did my net worth change? | 3.3 | — | Portfolio Performance categories | engine: `NetWorthBridge`, not wired |
| M6 | Gain vs cost | How much am I up on what I hold? | unrealized = value − average cost; realized = proceeds − average cost of what was sold; plus income and currency effect | standard average cost | Ghostfolio, Paisa | per position (`PositionReturnCalculator`); engine: `GainVsCost`, not wired; #982 asks for it at header level |
| M7 | Allocation | What is my money in? | weight = value ÷ total by asset class, currency, source, convertibility. Liabilities shown separately; weights over gross assets | — | arithmetic | partly (sleeves) |
| M8 | Risk | How bumpy is it, and how deep are the falls? | Volatility = standard deviation of monthly TWR returns × √12 (the "3-year" figure needs 36 months). Max drawdown on a flow-neutral index. Downside deviation over *all* observations | GIPS 4.A.1.j; empyrical definitions | empyrical-reloaded, quantstats | the invested book's current drawdown (`BookDrawdownCalculator`, risk rule); engine: `RiskStatistics` (volatility, 3-year volatility, downside deviation, max drawdown), not wired |
| M9 | Benchmark-relative | Did I beat the simple alternative? | Excess = book TWR − benchmark total return over the identical window and periodicity. Benchmark is SPY (C3) | GIPS 2.A.18 | empyrical | Book vs SPY's `delta` (`ExcessReturn`: book TWR − SPY adjusted-close return over the book's own dates); per decision, the Ledger scoreboard (`get_ledger_scoreboard`, a 30-day price excess per proposal or pick, money-semantics §10) |
| M10 | Projection | Where am I heading? | A savings path plus a stated return assumption, one shared assumption set, shown as a range (C8) | FINRA 2210(d)(1)(F) / 2214 pattern; DOL lifetime income illustration | — | tile + FIRE, two assumption sets (G7) |

Choices the engine fixes once, so they stop being re-decided:

- **Day count:** actual/365 (what Excel, LibreOffice and ExcelFinancialFunctions use).
- **Annualising:** never for windows under one year (GIPS 2.A.12).
- **Failures are typed.** An XIRR with no sign change, no convergence or a zero base returns a reason, not a
  number and not an exception. Never return `0` on bad input.
- **Return base:** a window starting at a value of zero (a new account) has no percent. Show the money
  figures with reason `ZERO_BASE`.

**The engine.** `backend/src/FinanceSentry.Core/Measures/` holds M1-M6, M8 and M9 as pure static calculators,
built the way `BookDrawdownCalculator` is: no I/O, no clock, no shared precision setting. Inputs are USD
values (C4: conversion for display is not the engine's job) and flows already classified into the five
classes for the scope asked; classifying provider events (3.2 rule 1, 5.2) comes before it and is not built.
Each measure returns either a value or a `MeasureFailure` reason (`ZeroBase` is `ZERO_BASE`, and so on). It
also fixes:

- **Values are decimal**; only the XIRR root finder works in double, as its source does.
- **XIRR** (`Xirr.cs`) is ported from ExcelFinancialFunctions (Apache-2.0; attribution in the file and in
  `backend/THIRD-PARTY-NOTICES.txt`): Newton's method, then bracketing and bisection. One-sign cash flows
  fail `NoSignChange` and a search without a root fails `NoConvergence`. M4 dates an inflow the day before
  it lands, so it is in place for the whole day, as rule 7 places it.
- **A missing value inside the window** (a day with no price) is bridged: the sub-period runs to the next
  valued close and the day is listed. A missing first or last value fails `MissingValue`.
- **A negative value** anywhere in a TWR window fails `NegativeBase`: a ratio through a negative base has no
  meaning. Value appearing in an empty scope with no recorded inflow is listed as unexplained and left out of
  the chain (rule 4), never chained as a return.
- **M8 (`RiskStatistics`)** is our own implementation of the empyrical definitions, with empyrical-reloaded
  0.7.0 as the offline oracle for its golden fixtures (nothing of it, or of LEAN's `Statistics.cs`, is in the
  repo). The observations are the time-weighted returns of each calendar month that opens and closes on a
  month-end close (the first, partial month gives none; a month-end close is the month's last calendar day or the
  last weekday before a weekend month-end, so a mid-month close is a stub and the returns beside it are left out,
  which can leave gaps in the series; a gap of a month with no valuation at all fails `MissingValue` rather than
  being bridged into a two-month return). Volatility is the sample standard deviation (n − 1) × √12; downside deviation is √(mean of
  min(r, 0)² over *all* observations) × √12 at a target of 0, so up-months count as zero shortfall instead of
  leaving the denominator; fewer than two returns fails `NotEnoughHistory`. Whole-history volatility and downside deviation use every
  valid return. The three-year volatility is over the 36 calendar months ending at the last valid month-end and
  fails `NotEnoughHistory` if any of those 36 monthly returns is missing or excluded, so 36 returns spread over a
  longer span never qualify (GIPS 4.A.1.j): the surface shows "not enough history",
  never a number. Max drawdown is the deepest fall of the chained time-weighted sub-period index (every valued
  close, so a dip inside a month shows), as a positive fraction; the index opens at 1, so a fall in the first
  step counts. `GrowthIndex` holds that chaining and `BookDrawdownCalculator` uses it for its current drawdown.
- **M6 splits each gain** into a price effect and a currency effect. With the position's local-currency
  amounts (each buy's and sell's, a sell's rate, and the held value with its rate), the price effect is the
  local gain converted at the end (or sale) rate and the currency effect is the USD gain minus it. A position
  given in USD only reports `UsdOnly`: its currency effect is 0 because it was not measured. Local amounts on
  some events and not others fail `InvalidInput`. A sale of the held quantity to within rounding (a reverse
  split by 1/3 leaves 0.999...) sells everything. Income is at its pay date's rate and is not split.

Golden fixtures live in `backend/tests/FinanceSentry.Tests.Unit/Core/Measures/`: `MeasureEngineGoldenFixtureTests`
holds the report's ten (R5) and `CryptoFlowGoldenFixtureTests` the crypto-flow fixtures 11-20. Each expected
value comes from a hand calculation or LibreOffice Calc's XIRR on the same flows, cited beside its
assertion, never from running the engine. The headline record (3.6) takes M1's amount, percent and failure
for the figures that are changes (net-worth history's window change, the `book` headline of `get_book_performance`);
the gain-vs-cost headlines describe the unrealized P&L the readers already sum (value minus cost of the
positions whose cost is known), not M6's event walk, which no reader feeds yet.

### 3.5 Worked example (fictional numbers)

| Date | Event | Value |
|---|---|---|
| 1 Jan | start | 10,000 |
| 1 Jul | markets +10% | 11,000 |
| 1 Jul | deposit +10,000 | 21,000 |
| 31 Dec | markets −5.71% | 19,800 |

| Measure | Working | Result |
|---|---|---|
| M1 value change | 19,800 − 10,000 | **+9,800 (+98%)**, labelled "includes deposits" |
| M2 contributions vs growth | contributions +10,000; growth = 9,800 − 10,000 | **put in 10,000, growth −200** |
| M3 TWR | (11,000 ÷ 10,000) × (19,800 ÷ 21,000) − 1 = 1.10 × 0.942857 − 1 | **+3.71%** |
| M4 Modified Dietz | −200 ÷ (10,000 + 0.5 × 10,000) | **−1.33%** |
| M4 XIRR | solve 10,000·y² + 10,000·y = 19,800 with y = (1 + x)^0.5 | **−1.33%** a year |
| Book vs SPY's book side before C2(a) | (19,800 − 10,000) ÷ 10,000 | **+98%**: a value change, not a TWR (it now reads the M3 +3.71%) |

TWR and MWR differ because twice as much money was invested during the losing half as during the winning
half. TWR ignores that, so it is the number to compare with SPY; MWR is the honest answer to "what did my
money earn". Both are correct; they answer different questions. This example is golden fixture #1 for the
measure engine (`MeasureEngineGoldenFixtureTests`). Its XIRR there is −1.328%, not the idealised −1.332%:
the real dates put the deposit 181 days after the first flow (exponent 181/365), not exactly half a year.

### 3.6 The headline contract

Every top-line figure carries the same record: the dashboard hero, the accounts and investments headers,
every MCP tool that returns a total or a return, and the briefs. **Status: built in the backend and MCP
(R6, first PR) and rendered in the UI (R6 P9: `fns-headline-badge` and the reason sheet).** The shape is paraphrased from
Wealthfolio's performance-semantics design (AGPL: design only, no code).

```
Headline
  amount       decimal?   in `currency`; null = unavailable
  percent      decimal?   null unless the method and coverage allow one (D4, D5)
  method       VALUE | VALUE_CHANGE | CONTRIBUTIONS_VS_GROWTH | TWR | MWR_XIRR | MODIFIED_DIETZ | GAIN_VS_COST | HPR
  basis        what the percent is "of": START_VALUE | COST | NET_CONTRIBUTED | AVERAGE_CAPITAL
  scope        umbrella | invested_brokerage | source:<id> | account:<opaque id> | sleeve:<name>
  window       from, to
  quality      COMPLETE | ESTIMATED | DEGRADED | UNAVAILABLE
  basisStatus  COMPLETE | PARTIAL | MISSING   (cost basis or flow history)
  coverage     share of the scope's value the figure actually measures, 0..1
  reasons      typed codes, never free text
  asOf         oldest input timestamp
  currency     the base the amount is expressed in
```

`VALUE` is a point-in-time value (net worth, a sleeve or split line): no window start, no basis, no percent.
`sleeve:<name>` covers today's sleeve totals (`sleeve:crypto`, `sleeve:brokerage`, `sleeve:banking`) and the
split lines (`sleeve:cash`, `sleeve:invested`) until R8's per-source rows replace the sleeves.

**Built (R6, first PR).** `Headline` and its enums live in `FinanceSentry.Core.Headlines` and serialise as the
names above (`JsonStringEnumMemberName`). Amounts are computed in USD (`HeadlineFactory`) and converted once at
the display boundary to the base currency (`HeadlineFactory.InBaseCurrency`, C4), which adds `FX_TODAY_RATE`
for a window reaching before today (today's rate, not the dated one) and the base's own FX status. A percent is
in percentage points, shares the amount's numerator (D1), is never annualised (D6) and is null with a reason
when the basis does not support one (D4, D5). `HeadlineSignals` gathers a figure's signals and
`HeadlineQualityMapper` is the single place that turns them into quality, basis status and reasons; the
quality is the worst its reasons imply and a figure with no amount is UNAVAILABLE, always with at least one
reason (`NOT_MEASURABLE` when nothing else explains it). It is an additive field beside the existing ones:

| Surface | Field | What it describes |
|---|---|---|
| Dashboard (`GET /dashboard/aggregated`) | `netWorthHeadline`, `cashHeadline`, `investedHeadline` | the hero (`umbrella`) and the C5 split lines (`sleeve:cash`, `sleeve:invested`; `NO_SPLIT` when withheld) |
| Wealth summary | `headline` | the accounts header total; `source:<provider>` / `sleeve:<category>` when filtered |
| Crypto and brokerage holdings | `headline` | the investments headers' sleeve totals |
| Net-worth history | `changeHeadline` | first to last point of the window, M1 (`SCOPE_CHANGED` from R3 withholds the percent; a window with one point is UNAVAILABLE with `SHORT_HISTORY`, not a zero change) |
| `get_portfolio_snapshot` | `headlines` (`totalValue`, `investedValue`, `cash`, `unrealizedPnl`) | the book totals; `unrealizedPnl` is `GAIN_VS_COST` with `basisStatus` and `coverage` |
| `get_net_worth_history` | `headline` per point | each day's total |
| `get_book_performance`, `GET /radar/book-performance` | `book` per period | the change in invested value, M1 over `invested_brokerage` (`SHORT_HISTORY`, `ZERO_BASE` or `SCOPE_CHANGED` when there is no percent); the TWR's own reasons are in `bookTwrReasons` and its quality in `bookTwrQuality`, which the growth of 10,000 card turns into a `TWR` headline for its badge |
| Brokerage growth (`GET /wealth/brokerage/growth`) | `headline` | M2's growth with the Modified Dietz percent, scope `sleeve:brokerage` (`UNKNOWN_FLOW` naming each provider with no deposits on record, `SCOPE_CHANGED` on a link) |
| Crypto growth (`GET /wealth/crypto/growth`) | `headline` | the same, scope `sleeve:crypto` (`UNKNOWN_FLOW <venue>` while its walks are behind; `UNMATCHED_TRANSFER <venue>` for a transfer leg read as external for want of a match) |
| `get_allocation_vs_target` | `totalHeadline` | the total the drift weights are taken over |

**Reasons are typed codes.** They become words through the frontend's existing registry pattern
(`ERROR_MESSAGES` / `ErrorMessageService`), never by parsing strings. Today's scattered signals map onto
them:

| Today | Becomes |
|---|---|
| `StaleSleeves` (comma-separated string on `NetWorthSnapshot`) | DEGRADED + `STALE_SOURCE`, per source |
| `IsApproximate` (bool; backfill from #584) | ESTIMATED + `BACKFILLED_ESTIMATE` + `FX_TODAY_RATE` |
| `CashTotal` null | `NO_SPLIT`: cash and invested splits are UNAVAILABLE |
| `PositionReturns.Coverage` | basisStatus COMPLETE / PARTIAL / MISSING |
| No known cost basis on any position | UNAVAILABLE + `NO_COST_BASIS` (amount and percent null, basisStatus MISSING) |
| Unknown currency converted 1:1 (`CurrencyConverter.Convert` → `UNKNOWN_1TO1`) | DEGRADED + `FX_UNKNOWN_1TO1` (reason code live, R4) |
| Historical FX replaced by today's rate (`TODAY_RATE`) or a seed-table rate (`SEED_TABLE`) | ESTIMATED + `FX_TODAY_RATE` (reason code live, R4) |
| `HasStaleSleeves` on FIRE | DEGRADED + `STALE_SOURCE` |
| "Book N/A" | UNAVAILABLE + `NO_SPLIT` (the period has snapshot days but no measured cash/invested split to form the window) or `SHORT_HISTORY` (no measurable window otherwise) |
| A measure's failure (`MeasureFailure`) on a change | the amount stands, the percent is null: `ZERO_BASE`, `NEGATIVE_BASE`, `SCOPE_CHANGED`, `SHORT_HISTORY` (a missing end: UNAVAILABLE), `NOT_MEASURABLE` |
| A source whose reading is an estimate (reliability L3: a value entered by hand) | ESTIMATED + `ESTIMATED_VALUE <source>` on the total and on the split line its value sits in (live, C6) |
| (new) | `UNKNOWN_FLOW`, `UNMATCHED_TRANSFER`, `NON_CASH_INCLUDED`, `PRICE_CARRIED_FORWARD`, `RESIDUAL_ABOVE_TOLERANCE` |

Every row above except FIRE is mapped in `HeadlineQualityMapper` (tests: `HeadlineQualityMapperTests`).
A live total's stale source is a sleeve whose read failed (`BookFigures.StaleSources`), a provider past its
freshness window, or an institution whose sync status is stale, failed or needs re-authorisation. The
`(new)` codes have their quality fixed (ESTIMATED for `UNKNOWN_FLOW`, `UNMATCHED_TRANSFER`,
`PRICE_CARRIED_FORWARD`; DEGRADED for `NON_CASH_INCLUDED`, `RESIDUAL_ABOVE_TOLERANCE`). `UNKNOWN_FLOW` is raised by
the brokerage and crypto growth headlines and Book vs SPY's TWR (C2(a)), `UNMATCHED_TRANSFER` by the crypto growth
headline (S2b); the others wait on the source registry (R8).

**Not yet carrying the record** (R6's remaining work): the FIRE projection and its `HasStaleSleeves`;
the briefs (`PerformanceBriefComposer`, the FIRE brief); the MCP tools whose figures are per thesis, per
recommendation or per flow rather than a book total or return (`get_benchmark_track_record`,
`get_thesis_performance`, `get_track_record`, `get_ledger_scoreboard`, postmortems, `get_cashflow_report`, budgets, the family
clearing statement) and per-row figures (positions, accounts, tax lots); a net-worth change headline on
`get_net_worth_history` (the tool returns a bare list); the holdings-based return on
`get_book_performance` (`holdingsBasedReturn`; the method label for a chained, dividend-excluded holdings price
return is the next PR's choice); and dated-rate display conversion.
**P9 (done)** renders `quality` as a badge on the dashboard hero, the cash / invested split line and the change
line, the accounts header and the investments header (`fns-headline-badge`, `shared/components/headline-badge`).
COMPLETE shows nothing, ESTIMATED reads "Estimated", DEGRADED reads "Stale" (a `STALE_SOURCE` reason) or "Partial"
(any other), and an UNAVAILABLE figure reads "N/A", never 0 or 0%. One tap opens a reason sheet (a
`CmnDrawerService` drawer) listing each reason in plain words, keyed by code in
`core/errors/headline-reasons.registry.ts` and resolved through `ErrorMessageService` (an unknown code gets a
generic line), plus `asOf` and `coverage` when the figure is not complete. The badge and its sheet hold a 44 x 44
px hit area. The ad-hoc stale alert under the dashboard chart (`netWorthStaleNotice`) is gone: the
latest history row's stale sources (its `sources` marked `STALE_SOURCE`; `staleSleeves` on an older API build) become
`STALE_SOURCE` reasons on the net-worth headline when the API's own record has none, and the investments total names a feed that failed to read as `STALE_SOURCE` the same way (a broken
connection is no longer buried mid-list, finding 5; a failed feed reads N/A with its badge, never a 0 total). The dashboard's "Change" line wears the `changeHeadline` badge; its figure still comes from the snapshots (G3 stays open).
The FIRE tile keeps its own `hasStaleSleeves` flag until the FIRE projection carries a record.

**Display rules:**

| Rule | What it says |
|---|---|
| D1 | One label, one numerator. "Change" is always M1, "Growth" is always M2, and "Return" always names its method in a secondary label. An amount and a percent on the same line share the same numerator. |
| D2 | Never hide a sign. Show + and −, and normalise −0 to 0. A liability reads `-$917.82`, never "Owes". |
| D3 | All-time on a holding means gain vs cost (M6). A bounded period means value change over the start value (M1) or a named return (M3/M4). Never mix them on one line. |
| D4 | A figure with mixed scope is never labelled TWR. A percentage appears only when coverage is coherent: the whole scope is measured, or the unmeasured part is named. |
| D5 | Missing cost basis shows "N/A" with its reason, never 0%. |
| D6 | No annualising under one year (GIPS 2.A.12). Shorter windows show the cumulative figure. |
| D7 | Unavailable, loading and zero look different: a skeleton while loading, "—" with a reason when unavailable, "0" only for a real zero. |
| D8 | An ESTIMATED or DEGRADED figure carries a badge, and the reason is one tap away. |
| D9 | Big headline numbers may use compact notation on phones ("$1.2M"), with the full figure one tap away, through `MoneyUtils` / the `money` pipe. Never compact a figure the person reconciles against a statement. |

### 3.7 Presentation patterns

A big number becomes meaning through a small set of patterns. Each needs its measure first; the ones built are
marked. R7 builds them in the order P3, P8, P2, P4, P5: P4 waits on the full bridge (M5 is in the engine, not
wired) and P5 on M10 and C8.

| Id | Pattern | What the person sees | Needs |
|---|---|---|---|
| P1 | Value-first rows | Each row's main figure is its value; quantity and price are secondary | valuation |
| P2 | Growth of 10,000 | What 10,000 invested at the start would be worth now, book vs benchmark | M3, M9. **Done (R7)**: Home Investments card, the longest Book vs SPY window measured for both, in USD; end values only, no line yet |
| P3 | Contributions vs growth | "You put in X; it grew by Y" | M2. **Done (R7)**: Home Investments card, a card per sleeve (brokerage, crypto) |
| P4 | Bridge waterfall | Start → savings → investment return → FX → linked → end | M5 |
| P5 | Monthly-income lens | A large total also shown as the monthly income it could pay | M10, C8 |
| P6 | 1 / 5 / 10-year table | Average annual total return per standard window, book vs benchmark | M3, long history |
| P7 | Checklist score | A few yes/no checks per holding, drawn as a shape; ideas only, thresholds cited (C9) | cited thresholds; fits #824 |
| P8 | Timing gap | "Your investments returned A; your money earned B; the difference is timing" | M3, M4. **Done (R7)**: under each growth split, when both returns are measured |
| P9 | Quality badges | "Estimated", "Stale" or "Partial" with a reason sheet | headline contract. **Done (R6 P9)** |
| P10 | Liquid vs total | Total net worth as the headline, "of which liquid" beneath (C5) | convertibility |

The Simply Wall St model behind P7 is licensed CC BY-NC-SA: take ideas only, no checks or thresholds
verbatim.

---

## 4. Licensing posture (C7)

- **Designs** may be taken from AGPL/GPL projects (Wealthfolio, Ghostfolio, Sure, Paisa, rotki, hledger,
  beangrow): paraphrase the idea, copy no code.
- **Code** may be taken only from permissively licensed projects (ExcelFinancialFunctions Apache-2.0,
  empyrical-reloaded and quantstats Apache-2.0, QuantConnect LEAN Apache-2.0), with attribution.
- **Reimplement from the method** where the source is copyleft or weak-copyleft (Portfolio Performance,
  EPL-1.0): re-implement the method and use the project as a cross-check.
- Public-repository licensing itself is open (C7(ii)).

---

## 5. Layer 2: the economy adapter contract

**Status: partial.** The registry is built (R8 PR 1, §5.6): the banking, brokerage and crypto readers are
registered as `IWealthSource` (`BankingWealthSource`, `BrokerageWealthSource`, `CryptoWealthSource`) and
`BookFiguresService` sums whatever is registered; the snapshot job writes one `net_worth_snapshot_sources` row
per registered source (R8 PR 2), which history, the dashboard, MCP and FIRE read back (R8 PR 3). The contract below is still the target: today a source
returns `WealthSourceReading` (USD cash, invested positions, worst FX status), not the full
`SourceSnapshot`, and has no `asOf`, native units, reliability or events.

### 5.1 What every source supplies

A source declares which facets it can supply; the engine computes only the measures those facets support.

| Facet | Contents | Required | Enables | Today's equivalent |
|---|---|---|---|---|
| Identity | source id, economy kind, native unit(s) | yes | — | provider name strings |
| Holdings | asset, quantity, native unit, exit price, price time, price source | yes | value, M1, M7 | `BankingAccountSummary`, `BrokerageHoldingSummary`, `CryptoHoldingSummary` |
| Cash and liabilities | cash balances; debts, signed as in money-semantics §1 | when the economy has them | liquid net worth, the cash/invested split | the cash split in `BankingWealthSource`, `BrokerageWealthSource`, `CryptoWealthSource` |
| Flows | dated movements with a counterparty reference and transaction id | no | M2, M3, M4, M5 | IBKR and Inzhur cash rows read by `BrokerageFlowReader`; venue fiat legs, Binance coin deposits and withdrawals and Revolut X transfers read by `CryptoFlowReader`, transfers between the venues matched by hash or by amount and time, and earn/airdrop/dust events (S2a, S2b, S2c; G6) |
| Income and costs | dividends, interest, rewards, fees, taxes | no | bridge lines, M6 | dividend and withholding rows read by `PositionReturnCalculator` |
| History | past snapshots, or a way to rebuild them | no | windows longer than the link's age | daily snapshots; the banking backfill (#584) |
| Cost basis | average cost per open position | no | M6 | nullable `CostBasisUsd` |
| Valuation metadata | reliability L1–L3, convertibility (5.3) | yes | quality, the liquid split | none |
| Health | connection-health state; a failure classifier and retry settings (5.7) | yes | `STALE_SOURCE` | `ConnectionHealthPolicy` states `Healthy`, `Degraded`, `Failing`, `ActionRequired`, `Dormant`; `StaleSleeves` |

**A missing facet makes the measures that depend on it unavailable, never zero.** A source with holdings
but no flows still counts towards net worth; in the bridge its value change shows as its own line, "change
in <source>, flows not available"; any percentage return whose scope includes it is UNAVAILABLE with reason
`UNKNOWN_FLOW`.

### 5.2 The contract (illustrative C#)

```csharp
public enum EconomyKind { Bank, Brokerage, CryptoVenue, ChainWallet, Fund, Marketplace, Manual }
public enum Reliability { L1QuotedIdentical, L2Observable, L3Estimated }   // GIPS 2.B.6, IFRS 13 levels
public enum Convertibility { Cash, InEconomy, Locked }                     // 5.3 step 4
[Flags] public enum SourceCapabilities
{
    Holdings = 1, Cash = 2, Liabilities = 4, Flows = 8, Income = 16, History = 32, CostBasis = 64,
}

public sealed record SourceSnapshot(
    string SourceId, EconomyKind Kind, DateTimeOffset AsOf, SourceCapabilities Capabilities,
    IReadOnlyList<SourcePosition> Positions, IReadOnlyList<SourceEvent> Events,
    ConnectionHealthState Health);

public sealed record SourcePosition(
    string AssetId, decimal Quantity, string NativeUnit,
    decimal? ExitPriceNative, DateTimeOffset? PricedAt, string PriceSource,
    Reliability Reliability, Convertibility Convertibility, decimal? CostBasisNative);

// The raw event as the provider reports it. The engine classifies it into the five flow classes
// per scope at measure time (3.2 rule 1); the adapter never decides "external" or "internal".
// Built in code: FinanceSentry.Core/Interfaces/SourceEvent.cs.
public sealed record SourceEvent(
    DateTimeOffset OccurredAtUtc, string ProviderType, decimal Amount, string Unit,
    string? CounterpartyRef, string? TxRef,
    string? Network, decimal? Fee, string? FeeUnit, string? Status);

public interface IWealthSource
{
    string SourceId { get; }
    Task<SourceSnapshot> ReadAsync(Guid userId, DateOnly asOf, CancellationToken ct);
}
```

- **Registration uses the platform's mechanism.** Each module registers its sources as multiple
  `IWealthSource` services and the aggregator takes `IEnumerable<IWealthSource>` (standard .NET dependency
  injection). Adding an economy means adding a module and a registration, not a schema change.
- **Owner scoping.** The daily snapshot job calls `ReadAsync` with no principal, so implementations use the
  `…Unscoped…` repository reads, each with its own `UserId` predicate
  ([backend-rules.md](claude/backend-rules.md), owner query filter).
- **`SourceEvent` fields.** `OccurredAtUtc` is a UTC instant, not a date: the 24 h transfer-match window and
  in-transit detection need the time of day (a date-only provider reports midnight UTC). `TxRef` is the raw
  hash as reported and is normalized only at match time, never stored normalized. `Network`, `Fee` and
  `FeeUnit` are null when the venue does not report them; `FeeUnit` can differ from `Unit` (a BNB fee on a
  USDT trade). `Status` is the provider's own string, kept raw like `ProviderType`. The fields cover exchanges
  only (Binance, Revolut X); self-custody and chain-indexer fields are out of scope.
- **Classification happens in the engine, not the adapter.** Adapters pass on the provider's own type
  string (IBKR Flex already stores it raw in `BrokerageCashTransaction.TransactionType`). A per-provider
  map turns those strings into the five classes. Sum *signed* amounts per class and decide income and fee by
  label, not by sign, so a fee rebate nets correctly.
- **Adapters report native units and exit prices.** Conversion to the base currency happens once, in
  normalization step 5. During the migration (5.6, PR 1) the wrapped readers may keep returning USD values
  as they do today.

### 5.3 The valuation rule for any economy

For every position, apply five steps in order:

1. **Principal market, in the economy's own unit.** The market where the holder would normally sell (IFRS
   13: the principal market, else the most advantageous one): a share on its primary exchange; a token on
   the venue that holds it; a fund unit at the fund's published redemption price; a marketplace item on
   that marketplace's own market, in the wallet's currency.
2. **The exit price, as the holder would receive it.** Fair value is not adjusted for transaction costs
   (IFRS 13 ¶25): use the market price, not the price minus the broker's commission. Where the *buyer* pays
   a fee the seller never receives, the holder's price is the listed price divided by (1 + the fees that
   apply). Fictional example: an item listed at 115.00 with a 5% and a 10% fee is worth about 100.00 to its
   holder.
3. **Reliability level** (GIPS 2.B.6; IFRS 13 levels 1–3):
   - **L1**: a quoted price for the identical asset in an active market (a listed share, a liquid token, an
     item with recent sales).
   - **L2**: observable but not an identical active quote (a thin market, a similar item, a fund's
     published unit price).
   - **L3**: an estimate (a manual value, a model). L3 makes the figure ESTIMATED.
   - A price older than its source's freshness limit carries `PRICE_CARRIED_FORWARD`.
4. **Convertibility: a separate axis from price.**
   - **CASH**: can become withdrawable money within days at about the quoted price (bank balances, broker
     positions and cash, tokens or fiat on a venue that allows withdrawal).
   - **IN_ECONOMY**: spendable only inside its own economy (a marketplace wallet that cannot be withdrawn).
   - **LOCKED**: convertible later but not now (a staking lock-up, an item under a trade hold, a fund with a
     redemption notice period).
   - A holding its owner cannot sell at all has no exit price and is not counted.
   - **Caps and frictions are recorded but never used to discount value.**
   - **No discount for size (blockage).** ASC 820 prohibits it, and IFRS 13 excludes it. Convertibility
     tells the person what is hard to sell; the value stays price × quantity.
5. **Base currency at the valuation date.** Convert with the rate *published for that date*
   (`IHistoricalExchangeRateService.GetPublishedRateAsync`). A token converts through its quote on its
   principal venue. A unit with no route to the base currency is not valued: UNAVAILABLE with reason
   `FX_UNKNOWN`. **Never 1:1.**

**Totals** (C5):

- *Total net worth* = every position with an exit price, whatever its convertibility, minus liabilities.
  This is the headline.
- *Liquid net worth* = CASH positions minus liabilities. Shown beneath as "of which liquid".

### 5.4 Source map

| Source | Kind | Holdings | Flows | Reliability | Convertibility | Status |
|---|---|---|---|---|---|---|
| Monobank; TrueLayer-connected banks | Bank | balances | bank transactions (money-semantics §4–5); "external" needs the counterparty rules | L1 | CASH | built; wrap as a source |
| IBKR | Brokerage | positions + cash | read by `BrokerageFlowReader`; the "Deposits & Withdrawals" type is not in the Flex query yet (C1) | L1 | CASH | deposits arrive once C1 is done |
| Inzhur | Fund | units (`InzhurHoldingsMapper`) | deposits land in the same table, read by `BrokerageFlowReader` | L2 (published unit price) | CASH or LOCKED, **not verified** (redemption terms) | wrap; check redemption terms |
| Binance, Revolut X | CryptoVenue | balances, venue fiat | fiat legs, coin deposits and withdrawals, Revolut X transfers, earn and staking rewards, airdrops, dust service charge, Binance spot trade commission (`CryptoFlowReader`, S2a/S2b/S2c) | L1 for liquid pairs | CASH; LOCKED for lock-up products | Revolut X trade fees, unvalued Binance commissions and on-chain yield stay in the market remainder |
| Self-custody wallets | ChainWallet | — | — | — | — | not built; needs research |
| Steam | Marketplace | wallet + inventory | wallet top-ups, market sales: unverified | L1–L3 per item | IN_ECONOMY; LOCKED under a trade hold | **not linked (C6)**: entered by hand through the manual source |
| Manual (a Steam wallet; property, pensions, private holdings) | Manual | value the person enters, in its currency, as of a date | none | L3 | as declared: CASH, IN_ECONOMY or LOCKED | built (`ManualWealthSource`, source id `manual`; money-semantics §8 "Values entered by hand") |

### 5.5 Steam: entered by hand, and the rule if it is ever linked

**Decided (C6, 2026-10-18): Steam is not linked.** The person enters it through the manual source: one value,
what they would receive after the marketplace's fees (step 2 of 5.3, applied by the person), L3, IN_ECONOMY.
It counts in total net worth, stays outside cash and liquid net worth, and makes the total ESTIMATED with
`ESTIMATED_VALUE manual`. Bank-side Steam charges stay spending at the bank layer (nothing is reclassified),
and the entered value joins the umbrella as a newly linked line on the day it is added (§3.2 rule 3: the
history change across that day withholds its percentage with `SCOPE_CHANGED`), so a top-up is never counted
as both spending and growth. Editing the value later is a revaluation (§3.3), not a flow.

The table below is the valuation treatment that would apply if Steam were ever linked; the verified facts,
the quotes and the list of what is not yet verified are in report §5.5 and §12.4.

| Holding | Value | Reliability | Convertibility |
|---|---|---|---|
| Wallet balance | balance, in its currency | L1 | IN_ECONOMY |
| Market items | exit price (5.3 step 2), on the marketplace's own market | L1 with recent sales; L2 thin or similar; L3 otherwise | IN_ECONOMY; LOCKED under a trade hold |

- **Totals.** Included in total net worth, excluded from liquid net worth.
- **Classification is the question that matters most** if it is ever linked. Today a card payment to a marketplace is spending at
  the bank layer. Once the marketplace is linked, a wallet top-up is an *internal* transfer at the umbrella
  scope and an item purchase is an asset purchase, while a game purchase stays spending. Linking would
  therefore change "monthly outflow" for anyone who buys items.
- **Not verified, and part of the research:** the inventory and price endpoints (undocumented), their rate
  limits, whether hidden and trade-held items appear, whether the terms allow a third party to aggregate
  inventory value, and how third-party cash-out marketplaces are treated.

### 5.6 From three sleeves to a registry (four PRs)

Before this work a fourth economy would have needed a new column on `net_worth_snapshots`, a new name inside
the `StaleSleeves` string, and a change in every consumer; now it needs one `IWealthSource` registration.

1. **Wrap. Done.** The three readers (`IBankingAccountsReader`, `IBrokerageHoldingsReader`,
   `ICryptoHoldingsReader`) are three `IWealthSource` registrations (`AddBookFigures`, which registers them in
   the order positions have always had: crypto, brokerage, banking). `BookFiguresService` loops over the
   registered sources; its consumers did not change. A source id is the name `StaleSources` has always
   carried (`banking`, `brokerage`, `crypto`), and a kind with no legacy slice (`EconomyKind`) counts in
   `CashUsd` and the totals only. `BookFiguresCharacterizationTests` pins the figures, and
   `BookFiguresWealthSourceTests` that a fourth registered source is summed.
2. **One row per source. Done.** `net_worth_snapshot_sources` (`WealthDbContext`, `public` schema, so no
   cross-module migration ordering; migration `M008_AddNetWorthSnapshotSources`): one row per
   `(snapshot, source id)`, unique, cascade-deleted with its snapshot. Columns: `source_id`, `kind`
   (`EconomyKind` by name), `total`, `cash`, `invested` (`numeric(18,2)`, USD like the snapshot's totals; null,
   never zero, when the source could not be read), `health` and `reasons` (`text[]`). The row has no `UserId`;
   the Owner filter reaches it through its snapshot. `NetWorthSnapshotJob` takes one `IBookFiguresService` read
   - `BookFigures.Sources`, what each registered `IWealthSource` read - and derives both the rows and the sleeve
   columns from it; `NetWorthSnapshotService` persists the snapshot and its rows in one transaction
   (`UpsertWithSourcesAsync`): a same-day refresh replaces the day's rows, and a fourth registered source gets its
   row with no change to the job. The sleeve columns (`BankingTotal`, `BrokerageTotal`, `CryptoTotal`) are the
   `banking`, `brokerage` and `crypto` readings' totals, `StaleSleeves` and `fx_status` are written as before;
   the holdings and sync-time reads that remain only decide freshness, since a reading carries no sync
   timestamp. A source that failed to read is not fresh, so its sleeve is carried forward instead of recorded as
   zero; with no earlier snapshot to carry it from, nothing is written and the run fails, so the job retries.
   - **Health** is the framework's quality vocabulary (§3.6) stored by name: `Complete`; `Estimated` (a
     today's-rate or seed-table conversion, reason `FX_TODAY_RATE`); `Degraded` (a 1:1 conversion,
     `FX_UNKNOWN_1TO1`, or a sleeve this snapshot carried forward, `STALE_SOURCE`); `Unavailable` (the read
     threw: null figures, `STALE_SOURCE`, the same way `BookFigures` reports a failed source stale).
   - **A row is what the source read this run.** When the snapshot carried a sleeve forward, the snapshot's own
     column holds the carried figure and the row says so through its health (a source beyond the sleeves that
     failed to read is carried in its own row instead, PR 4). A sleeve that was read,
     is fresh and was not carried forward is `Complete` or `Estimated`, and its row total is its column
     (`NetWorthSnapshotSourceRowTests` pins this through the job and the service, with a stale and a failed
     source); whenever every source is, the rows add up to the sleeve columns, up to a cent: each figure rounds
     to cents on its own.
   - **Totals.** A source beyond the three sleeves is recorded and read back per source (PR 3) and, since PR 4,
     counted in `TotalNetWorth` (see step 4).
   - **Not backfilled.** Past snapshots are untouched and have no rows (PR 3 treats "no rows" as "legacy").
     The startup catch-up (`NetWorthSnapshotBackfillService`) fills gap days through the same job, so those rows
     carry the figures read at that moment, like their snapshot columns. The history reconstruction command
     (`BackfillNetWorthHistoryCommand`, banking only, rebuilt from transactions) writes no per-source rows: it
     has no per-source figures without further provider reads.
3. **Read per source. Done.** One adapter, `SnapshotSources` (Wealth `Domain`), is the only reader of a stored
   snapshot's sources: a snapshot with rows reads each source's health and reasons from them; one taken before
   PR 2 (no rows) reads the three sleeve columns and `StaleSleeves` exactly as before (a listed sleeve is
   `Degraded` with `STALE_SOURCE`, the rest `Complete`). A sleeve's counted figure is its column whenever its row
   is stale or has no total (a carried-forward row holds this run's reading, not the carried one), so every
   figure is unchanged; a sleeve with no row of its own falls back to its column. Readers take stale state from
   the sources (`STALE_SOURCE` per source), never by parsing the column:
   - **Net-worth history** (`GetNetWorthHistoryQuery`, the API and the `get_net_worth_history` MCP tool) returns
     an additive `sources` list per row (`sourceId`, `kind`, `total`, `health` in the headline vocabulary,
     `reasons`), the three sleeves first. `staleSleeves` is still returned, derived from the sources (sleeves
     only, legacy order). The change headline, each MCP row's headline and the dashboard hero's stale badge take
     one `STALE_SOURCE` per stale source, so a source beyond the sleeves is named too. A non-USD base converts
     each source's total at the row's rate (C4); the conversion's own reason stays row-level in `fxReasons`.
   - **FIRE** (`HasStaleSleeves`), the **brokerage history reader** (Radar portfolio value; only the brokerage
     source's own staleness reaches its signals) and the **split backfill** read stale state from the sources.
   - `GetSourcesAsync` (Owner-filtered) and `GetSourcesUnscopedAsync` (jobs and cross-module readers, own
     `UserId` predicate) read the rows for a window in one query. `SnapshotSourcesTests` pins that a legacy
     snapshot gives the same stale state and signals as its columns, and the history handler test that a mixed
     window (pre-PR-2 day and a day with rows, a carried-forward source) returns the same figures and change
     headline as the column formula. The legacy columns are still written; nothing is migrated or backfilled.
4. **Freeze the old columns. Done.** `BankingTotal`, `BrokerageTotal`, `CryptoTotal` and `StaleSleeves` stay, and are
   still written exactly as before (older history and the PR 3 fallback depend on them), but they are never
   extended. No migration, no stored data changed.
   - **Marked in code.** Each carries `[FrozenLegacyColumn]` (Wealth `Domain`) naming the per-source row that
     replaces it. Not `[Obsolete]`: the writer and the fallback use them by design, so that would only add
     suppressions.
   - **Enforced.** `FrozenLegacyColumnGuardTests` (`Tests.Unit/Architecture`) fails when `NetWorthSnapshot` gains a
     property (a fourth sleeve) or when the set of properties carrying `[FrozenLegacyColumn]` changes.
   - **The totals.** `TotalNetWorth` is the sum of every registered source's resolved figure
     (`NetWorthSnapshotService`): the three sleeves resolve exactly as before (fresh figure, else the previous
     column, the first-day throw), and each other source counts as it read this run or, when its read failed, as
     its own previous per-source row counted it (the row is then `Degraded` + `STALE_SOURCE` and holds the carried
     figure; a source with no earlier figure counts nothing and stays `Unavailable`). History rows, its change
     headline and FIRE read that total, so a fourth source is in all of them; the live dashboard total adds
     each such source's book reading (`DashboardQueryService`), so it agrees with the next snapshot. `NetWorthSnapshotTotalTests` pins
     the three-source total unchanged and a fourth source counted, carried and in the history headline.
   - **The cash / invested split.** `CashTotal` is the sum of every source's own cash (the sleeves' cash as before,
     plus each other source's per-source cash, carried from its previous row when its read failed), and the invested
     parts are the sleeves' invested positions plus each other source's per-source invested value, so
     `CashTotal + BrokerageInvested + CryptoInvested + otherInvested = TotalNetWorth` with a fourth source present.
     The two sleeve invested columns stay the only stored invested columns (frozen); `otherInvested` is read from
     the per-source rows (`SnapshotSources.OtherInvested`). Stale or unknown-split handling is unchanged: a stale
     book leaves the split null. For the three original sources the stored values are identical to the former
     `total - brokerageInvested - cryptoInvested` remainder (`NetWorthSnapshotTotalTests`).
   - **Every reader routed through per-source figures** (none derives a figure from three sleeves or from
     total-minus-sleeves any more):
     - the snapshot writer's cash split (`NetWorthSnapshotService`);
     - the nightly capture's sleeve invested parts, taken from the `brokerage` / `crypto` sources' readings only,
       so another source's positions never reach a sleeve (`NetWorthSnapshotJob`);
     - the split backfill, which leaves a day that counted a source outside the three sleeves with a null split
       because Risk's `holding_snapshots` holds none of that source's positions (`NetWorthSplitBackfillService`);
     - net-worth history: `otherInvested` on the REST row and on the MCP `get_net_worth_history` entries
       (`GetNetWorthHistoryQuery`, `NetWorthHistoryController`, `GetNetWorthHistoryTool`);
     - the dashboard chart's split bands, which add an "Other sources" band from `otherInvested`, and the invested
       change headline, which counts it (`NetWorthSplitUtils`, `dashboard.computed.ts`);
     - the no-split chart (its "Other sources" band is each source's own total beyond the sleeve columns), the live
       dashboard total (`DashboardQueryService`), and FIRE, which reads `TotalNetWorth`.

### Adding a new economy

Register an `IWealthSource`; nothing else changes.

1. Complete the new-source checklist (§7) before writing the adapter: §5.4 row, facets (§5.1), reliability and
   convertibility (§5.3), failure classes (§5.7).
2. Implement `IWealthSource` and register it in `AddBookFigures` (its id is the `source_id`, its `EconomyKind`
   the row's `kind`). `BookFiguresService` sums it into the live book.
3. The snapshot job writes its `net_worth_snapshot_sources` row (total, cash, invested) from `BookFigures.Sources`;
   history, the dashboard, the cash / invested split, MCP and FIRE read it back through `SnapshotSources`.
4. Do not add a column to `net_worth_snapshots`, a name to `StaleSleeves`, or a read of the frozen columns; the
   guard test fails the build.

Rejected: a column per new economy (each economy then costs a migration and a change to every consumer) and
one JSON column (loses typed queries and per-source quality fields).

### 5.7 The resilience and retry contract

Most of the retry machinery exists; this section writes it down as one contract so a new source declares
how it *fails*, as well as what it *holds*. **Keep the libraries already in place** (no new library, no
bespoke engine): Microsoft.Extensions.Http.Resilience (Polly v8) inside the call, Hangfire across runs, and
`ConnectionHealthPolicy` to confirm a failure before anyone is told.

**The rule:**

1. Each source classifies every failure once, in its own classifier (`IProviderFailureClassifier`).
2. The class decides three things: whether the call retries, when the next run may try, and whether anyone
   is told.
3. Nobody is told about a single failure. The one exception is a definitive credential loss: the provider
   has said plainly that access is gone, so that signal is its own confirmation.

**Failure classes** (`RateLimited` and `NotReady` came with R12 PR 2; the early re-checks with PR 3; the pause on a lost credential with PR 1):

| Class | Typical signals | In the call | Across runs | Who is told, and when |
|---|---|---|---|---|
| **Transient** | 5xx, 408, network errors, timeouts; IBKR's "backend down" 401 | Exponential backoff with jitter, safe methods (GET) only; 2 retries by default | The next scheduled run; a once-a-day source also looks again earlier (early re-checks below). Once Dormant, an active provider's scheduled runs try it once a day | Nobody while Degraded. The outage alert once Failing (at least 3 failures and 2 h without success). Dormant after 7 days |
| **RateLimited** | 429; Monobank's rate-limit code; Flex 1018 | Wait as long as `Retry-After` asks if that fits the call's time budget; otherwise stop at once | No scheduled call before the time the provider named (`ConnectionHealth.NotBefore`, from `Retry-After`, as named; a bank sync is skipped, a sweep leaves the credential alone) | Counts like Transient; never a credential signal. A 429 that persists across runs reaches Failing like any outage |
| **NotReady** | Flex 1003, "statement is not available" | Poll while the provider says it is still generating (Flex: the retries of 1003 and 1019 inside a run) | Flex reschedules itself sooner (every 2 h, until the next daily run) instead of waiting for it | Not counted until the last success (the connection's creation, before the first one) is older than the source's NotReady deadline (48 h for Flex); then like Transient. A source with no deadline counts it at once |
| **Credential, definitive** | TrueLayer `invalid_grant` with a consent marker; Flex 1012–1015; a stored key that cannot be decrypted | Never retried | Scheduled calls pause, except one probe a day, until the person reconnects or a probe succeeds | The reconnect alert at once (ActionRequired) |
| **Credential, suspect** | 401/403 without proof: Monobank 403, IBKR and Revolut X 401/403, Flex 1020 | Never retried (repeated failed sign-ins are what trigger account lockouts) | An active provider's probe job (probe job below) makes a cheap authenticated call, first at the next :07/:37 tick at least 1 min after the failure that raised the suspicion, then at most one per 30 min window while the suspicion stands (a success clears it there and then; a failed probe that is a credential failure confirms it). Ordinary runs try again with a fresh session where the source has one (a once-a-day source: its early re-checks, so the confirming sighting comes the same day). A second sighting at least 30 min after the first also confirms it. Simultaneous failures across one bank's connections read as an outage | The reconnect alert once confirmed |
| **ResourceGone** | TrueLayer 404 on one account | Never retried | A slower cadence | Failing, then Dormant, for that account only |
| **Internal** | Parse errors; our own bugs | Never retried | The next run | Never the person; it goes to the operations path |
| **Unknown** | Anything not classified | Only if the HTTP status is transient | The next run | Counts like Transient, with neutral alert wording |

**Three layers, each on an established mechanism:**

| Layer | Time scale | Mechanism | What it decides |
|---|---|---|---|
| 1. In the call | seconds | The standard resilience handler (rate limiter, total timeout, retry, circuit breaker, per-attempt timeout) via `ProviderHttpResilience`; Monobank has its own pipeline | Whether to retry or give up, and how long to wait |
| 2. Across runs | minutes to hours | Hangfire: the recurring schedule is the retry loop; `[AutomaticRetry]` with explicit delays for a run worth repeating (Inzhur) | When the next attempt may happen |
| 3. Confirm and tell | hours to days | `ConnectionHealthPolicy` and `ConnectionHealthSyncFailureAlerts`, switched on per provider by `ConnectionHealth:ActiveProviders` | Whether, and how, the person is told |

**Defaults and per-source overrides.** The alert thresholds stay one shared set (the captain's decision of
2026-10-06, `ConnectionHealthOptions`). A source gets its own value only for what its cadence or protocol
makes its own:

| Setting | Default | Override, and why |
|---|---|---|
| In-call retries on safe methods | 2 | 0 for signed requests (IBKR, Flex, Binance, Revolut X): a replayed nonce or timestamp is rejected |
| Early re-checks after a failure (`EarlyRechecks`) | none | a once-a-day source re-checks 1 h after a counted failure, then 4 h after that (Flex; Inzhur when it joins the policy) |
| NotReady deadline, NotReady retry delay | none | Flex: 48 h, 2 h (`ConnectionHealthSourceOptions`, bound from `ConnectionHealth:Sources:<provider>`) |
| Pause on ActionRequired | yes (every scheduled sync) | — |
| Probe while ActionRequired | once a day, timed from `LastFailureAt` | shared (`ActionRequiredProbeInterval`) |
| Failing after / Dormant after / Suspect confirmed after | 3 failures and 2 h / 7 days / 30 min (shared) | none |
| Scheduled runs while Dormant | once a day, timed from `LastFailureAt`; active providers only | shared (`DormantSyncInterval`) |
| Probe of a suspect or Dormant connection | at most one per connection per 30-min window (job at :07 and :37), the first ≥ 1 min after the suspect failure; Dormant at its daily cadence; active providers only | shared (`ProbeInterval`); Monobank skipped while its client-info is cached (one call a minute per token) |

**Early re-checks** (`ConnectionHealthSourceOptions.EarlyRechecks`, R12 PR 3). A source that runs once a day
would otherwise reach Failing (3 failures and 2 h) on its third day. After a counted failure of a transient kind
(Transient, Unknown, or a suspect credential signal) on its daily run, the job schedules one earlier look with
Hangfire's `BackgroundJob.Schedule` at the first listed interval, and each look that fails again schedules the
next one: the list is the bound, so the chain ends (Flex: 1 h, then 4 h). A look goes through the same gate as
the daily run, so it waits out `NotBefore` and a pause, and does nothing once the connection is healthy again.
None follows a success, a RateLimited answer (it waits for `NotBefore`), a NotReady one (it has its own retry,
which is not duplicated), a definitive credential failure (the pause), or a failure that leaves the connection
Failing or Dormant (the alert is out, and the next daily run picks up a recovery). An interval
set in `ConnectionHealth:Sources:<provider>:EarlyRechecks` replaces the code default.

**Dormant cadence and the probe job** (connection-health slice S4), both only for a provider in
`ConnectionHealth:ActiveProviders`; a shadow provider's schedule is untouched, and one `Connection health (shadow)`
line says what either would have done. A Dormant connection is paused by the same gate as ActionRequired
(`ConnectionHealthPolicy.ScheduledRunPausedUntil`, `DormantPausedUntil`): one scheduled run a day, timed from the
last failure, so a failed run renews the pause, a success goes Healthy and resolves the alert through the usual path,
and a reconnect (which clears the last failure) lets the next run through. The bank scheduler and the discovery pass
apply it to Dormant accounts and connections the same way.

A suspect credential signal is confirmed (ActionRequired, the reconnect ask) when it is seen twice at least the
confirmation window (`SuspectConfirmationWindow`, 30 min) apart, or once plus a failed probe. The probe is the
recurring Hangfire job `connection-health-probe` (`ConnectionHealthProbeJob`, every `ProbeInterval` = 30 min, at :07
and :37, off the sync jobs' quarter-hours). Each sync module lists its suspect and Dormant connections
(`IConnectionHealthProbe`) and the job probes those due (`ConnectionHealthPolicy.ProbeDue`) with the provider's cheap
authenticated call, never a full sync: Monobank's client-info, IBKR's session token (`EnsureSessionAsync`),
Binance's account endpoint, Revolut X's balances (its smallest existing read, as it has no account-only call), and
TrueLayer's token refresh. A probe's credential failure on a suspect connection confirms it at once
(`ConnectionHealthPolicy.RecordProbeFailure`); a success clears the suspicion and resolves the alert; any other failure
(an outage, a rate limit) confirms nothing and is counted like a failed run. A Dormant connection is probed on the same
clock as its slowed run (timed from its last failure), but a successful probe never heals it: it clears a suspicion and
leaves the state, the failure streak and the last failure as they were, and resolves no alert, because the probe proves the
sign-in, not the resource whose failure made it Dormant. Only the success of the slowed run itself goes Healthy and
resolves. Probes never hammer a sign-in: at most one per connection per window
(the first may follow the suspect failure after 1 min; after any later failed attempt, the next waits for the next
window), none for a connection that is neither suspect nor Dormant, none while the provider's `NotBefore` holds, none
while ActionRequired (its daily scheduled run already probes it), and a Monobank token whose client-info is still
cached is left for the next window. The bank probes run for suspect connections only, and TrueLayer's refresh (which
rotates the refresh token) is never made otherwise: a Dormant bank connection is already looked at once a day by the
discovery pass, with these same calls. IBKR Flex and Inzhur have no probe: Flex's only call generates a statement,
so its daily run and its own early re-checks (1 h, then 4 h; a suspect 1020's first look already confirms) are its looks.

**What the person is told** (connection-health slice S5), again only for a provider in
`ConnectionHealth:ActiveProviders`: a shadow provider's labels, alerts, Companion relay and MCP read stay exactly
what its own sync path gives, except that an account its module holds Dormant (closed at the bank) is labelled
`dormant` on the Accounts page, so it does not fail its working siblings' institution; with the list empty nothing
else a person can see changes. Every surface reads the
health its module stores, through one port per module (`IConnectionHealthReader`: bank tokens, consents and
accounts; IBKR and Flex credentials; exchange credentials), and labels it with one map
(`ConnectionHealthSurface`), so the app, Companion and the agent cannot disagree:

| State | Label (Accounts page, MCP `health`) | In-app | Companion | MCP `get_sync_health` status |
|---|---|---|---|---|
| Healthy | none (the path's own "synced" / "stale") | nothing | nothing | `ok` |
| Degraded | none (told only once Failing) | nothing | nothing | `ok`, health `healthy` |
| Failing | Retrying, quietly | the `SyncFailure` alert (S2/S3) | digest; escalates once open > 24 h (existing reconciler) | `error` |
| ActionRequired | Reconnect needed (error colour, attention row) | the reconnect alert, linking `/accounts/list?reconnect=<provider>` | relayed at once in every mode, Quiet included | `error`, "Reconnection required." |
| Dormant | Dormant, quietly (neutral dot) on the account or connection | one info notice, titled "… may be closed", body "It has been failing for <period>[ while your other accounts sync fine]." | one digest line | `error` |

An account shows its connection's state while the person is told about it (Failing, ActionRequired, Dormant), else its own sub-state; a group (an institution,
a provider) shows its most urgent member, and a Dormant member is quiet beside working siblings, so one closed
account does not mark the rest, and a Dormant account never marks the headline (it is a quiet state). A failure the policy does not count (one of ours, a sync we
cancelled) is not shown as a failure at all. The reconnect link opens the provider's existing reconnect flow where
the app has one (TrueLayer, Inzhur) and otherwise lands on the Accounts list, whose row asks for the reconnect; no
new sign-in flow is started from it. The Dormant notice (`ConnectionDormant` alert) is raised on the transition,
once per connection or account ever (`AlertDedup.OncePerReference`), so a dismissed notice stays dismissed; it is
resolved when the connection syncs again. The notice only asks; it does not retire anything. Retiring a closed account
is the system's job when two independent signals agree (below), and the person's reversal is one tap, so it stays the
person's account.

**Retire and reopen (card and account lifecycle).** An account is retired automatically when (1) its own health is
Dormant with class `ResourceGone` (failing for `DormantAfter`, 7 days) **and** (2) its provider's last successful
account-list read no longer names it for 24 h or more while a sibling of the same connection is still listed. One
signal alone never retires: a Dormant account the list still names stays as it is, and an absent account that is not
Dormant (an empty or partial list) is only excluded from live totals. A failed list read changes nothing (a failed
TrueLayer `/cards` read never makes cards look absent). Retiring sets `ClosedAt` / `ClosedReason` / `ClosedBy`, stops
the sync, keeps every transaction as history, resolves the account's failure, Dormant, cash-shortfall and low-balance
alerts (resolved, not deleted) and raises one info notice, `AccountRetired` (digest only, never a push). The notice
is raised whether or not the provider drives the connection-health policy (Monobank is still in shadow mode). The
person can reopen it in one tap (`POST /accounts/{id}/reopen`), and it reopens by itself when the provider lists it
again, unless a reissue replaced it. A new account whose IBAN **exactly** equals that of a failing or absent account of
the same connection is its reissue: the old one retires at once (reason `Reissued`, no 7-day wait) and the new one
records it as `PredecessorAccountId`; a heuristic match without an IBAN (TrueLayer card: same type, currency and last
four) only suggests and never links. A card reissued on the **same** provider account id (Monobank) is no retirement:
discovery refreshes the last four in place and keeps the old digits as `PreviousLast4` (with `PanChangedAt`), so the
Accounts row can still read "New card •••• new (replaced •••• old)"; nothing is kept while the old card is still in the
provider's masked PAN list (a second card, a token or a reorder is not a reissue), and a linked predecessor takes
precedence over it.

**Failures and retries, per source** (each new source fills in this table, and a new source's job declares its
settings the way Flex does in `BrokerageSyncModule`):

| Source | In the call | Across runs | Classification | Settings (`ConnectionHealth:Sources:<source>`) | Policy-driven alerts |
|---|---|---|---|---|---|
| IBKR | No retries (signed); drops its cached session token on a 401/403 | Every 15 min; ActionRequired pauses it, one probe a day; Dormant, one run a day; a suspect 401/403 probed (session token) from the next :07/:37 tick at least 1 min after the failure, at most once per 30 min while suspect | 401/403 suspect; "backend down" 401 transient | shared defaults | Yes |
| IBKR Flex | No retries; limiter 1 call/s and 10/min; polls while generating | Daily 06:00; early re-checks after 1 h and 4 h; NotReady retried every 2 h until the next run | 1012–1015 definitive; 1020 suspect; 1003 NotReady; 1018 RateLimited; 1001, 1004–1009, 1019, 1021 transient | `EarlyRechecks` 1 h, 4 h; `NotReadyDeadline` 48 h; `NotReadyRetryAfter` 2 h | Active (`ibkr-flex` in `ActiveProviders`) |
| Monobank | Only 429, within 180 s | Per-account schedule, every 30 min | 403 suspect; rate limit RateLimited | shared defaults | Shadow |
| TrueLayer | 2 retries on GETs; the token POST never | Same schedule | `invalid_grant` with consent marker definitive; 401/403 suspect; 404 ResourceGone | shared defaults | Shadow |
| Binance, Revolut X | No retries (signed) | Every 15 min | Revolut X 401/403 suspect; unreadable key definitive | shared defaults | Shadow |
| Inzhur | Default handler's 2 retries | `[AutomaticRetry]` after 1 h and 4 h, then the outage alert; **not on the policy**: when it joins, move that onto `EarlyRechecks` (1 h, 4 h) and the policy | Its own `reauth_required` status | not yet | No |

The IBKR rule, "retry once, then confirm at 30 minutes", is one instance of this contract: a 401/403
without "backend down" is a suspect credential failure; the signed call never retries inside itself; the
client drops its cached session token, so the next 15-minute run starts fresh (that is the retry); a
credential failure seen again at least 30 minutes after the first, with no success between, makes the
connection ActionRequired and raises the reconnect alert; any success clears the suspicion.

---

## 6. Known gaps in today's code

Each gap names the measure or rule it breaks. The report (§6, §5.7) holds the evidence.

**G1. "Book vs SPY" is a change in invested value, not a time-weighted return.** *Closed (C2(a), 2026-10-15): `bookTwr` is now a TWR on the cash-inclusive brokerage total, against SPY over the same dates (money-semantics §10). The history below is kept as the record.*
The book side of `get_book_performance` and the weekly brief is last ÷ first − 1 over the invested
brokerage value (M1, not M3). Deposits and withdrawals are not adjusted for, so a contribution reads as a
gain: with the §3.5 example it reads +98% where TWR is +3.7%. Two further distortions come from scope: the
series excludes idle broker cash, so a purchase from cash already in the account reads as a gain (and a sale
as a loss) with no deposit at all; and the SPY side uses adjusted closes, which include dividends, while the
book's dividends are paid into cash outside the series, so the comparison is a total return against a price
return. The `outperform` / `underperform` verdict inherits all of this.
*Done:* the descriptions and the brief label now say "change in invested value (includes deposits)".
*Not done, deliberately:* the type and field names `PeriodTwr`, `BookTwr`, `SpyTwr`, and the tool name
`get_book_performance`, keep their `Twr` names. They are misleading, and renaming them is a contract change
against the MCP tool surface and its tests.
*R1(b) done:* the same tool and brief now also carry a holdings-based price return (`HoldingsBasedReturnCalculator`,
the `BookDrawdownCalculator` method: revalue the earlier capture's quantities at the later capture's prices,
then chain), so a deposit or a purchase from idle cash reads 0%. It excludes dividends, so it is compared with
SPY's *price* return (close, not adjusted close) between the first and last capture the index covers, and it is labelled "holdings-based
return" vs "S&P 500 price return". It is on the invested brokerage positions (the holdings captures hold no
cash); the cash-inclusive scope (C3) and the position ledger's dividends are not in it. A missing price,
too few captures or an uncovered window yield a typed reason, never 0. A position sold inside the window is
carried at its earlier value for the step that sold it, so that step understates the position's move. Semantics:
money-semantics §10. *C2(a) done:* `bookTwr` is a daily-chained TWR on the cash-inclusive brokerage total with the
brokers' deposits and withdrawals taken out, so the `Twr` names are now true; SPY's adjusted close runs over the
book's own dates (`bookTwrFrom` / `bookTwrTo`), and dividends paid into cash are inside the scope, so it is total
against total. Until IBKR reports deposits (C1) it is an estimate (`UNKNOWN_FLOW`, "(est.)" in the brief).

**G2. Currency fallbacks are silent at the headline.** *Closed for the live total and stored snapshot rows (R4); per-source status is stored since R8 PR 2 and returned per source in history since PR 3.*
`CurrencyConverter.ToUsd` still returns an unknown currency's amount unchanged, but the headline readers now
use `Convert`, so `BookFigures`, the snapshot job and net-worth history say when a seed-table, today's-rate
or 1:1 conversion touched a figure (`FX_TODAY_RATE`, `FX_UNKNOWN_1TO1`). A snapshot row records the worst
status of its sources (`fx_status`; each per-source row carries its own status as health and reasons), and the amount-only `ToUsd` callers
outside the headline paths are unchanged. money-semantics §9 accepts the approximations.

**G3. The dashboard "Change" percentage counts newly linked accounts.**
(end − start) ÷ start over snapshots. Savings legitimately belong in a value change and the label is honest
about that (M1), but linking an existing account is not a change in wealth, yet shows as one, with a
percentage (rule 3 of §3.2; reason `SCOPE_CHANGED`).

**G4. The sleeves are hard-coded.** *Closed (R8 PRs 1-4): the live book sums registered sources, every
snapshot written since has a per-source row, the snapshot readers take sources and staleness from those rows
(§5.6 step 3), and the three sleeve columns are frozen and guarded (step 4).*
Three total columns on `net_worth_snapshots` and staleness as a comma-separated string of sleeve names, still
written. The columns are frozen (R8 PR 4, §5.6): a new economy is an `IWealthSource` recorded and reported per
source, counted in the stored total, never a column.

**G5. Quality signals are scattered and untyped.** *Closed for the headline figures, the MCP
totals and the UI badges (R6, first PR and P9); the FIRE tile still reads its own untyped `hasStaleSleeves`.*
`StaleSleeves` (string), `IsApproximate` (bool), `CashTotal` null, `Coverage` (string), `HasStaleSleeves`
(FIRE flag), "Book N/A" (null fields), FX substitution. The headline figures and the MCP tools that return a
total or a return now carry the typed record beside these (§3.6), so MCP reads one vocabulary; the untyped
fields stay for current consumers. The FIRE flag, the briefs and the per-thesis and flow tools are not yet
covered (§3.6, "Not yet carrying the record").

**G6. Investment flows land but nothing reads them.** *Partly closed (C2(a)): `BrokerageFlowReader` reads IBKR and Inzhur deposits, withdrawals, income, fees and taxes at each row's dated rate, feeding M2-M4 and Book vs SPY. Open: IBKR deposits still need C1; crypto flows (C2(b)) partly closed by S2a: `CryptoFlowReader` reads Binance fiat deposits, withdrawals and card buys and sells and Revolut X transfers into the crypto sleeve's M2/M4 (`GET /wealth/crypto/growth`); S2b added Binance's coin deposits and withdrawals, cross-venue matching by hash (or amount and time) with value in transit between venues, and `UNMATCHED_TRANSFER`, so the split is no longer withheld for Binance; S2c reads earn, staking and airdrop income and the dust service charge (ETH staking, WBETH and on-chain yield, Revolut X trade fees and Binance trade commissions stay in the remainder).*
IBKR and Inzhur cash rows land in `brokerage_sync."BrokerageCashTransactions"` and `BrokerageFlowReader`
classifies them. No IBKR deposits or withdrawals arrive, because the Flex query does not include that type
(C1). Crypto venues store their fiat legs and Revolut X's transfers (`crypto_sync."CryptoFlowEvents"`); Binance's coin deposits and withdrawals are read too (S2b), so a crypto scope computes M2 and M4; its earn, dust and airdrop events are read too (S2c).

**G7. Two projections use two different assumptions.**
The 12-month tile is median monthly savings × 12 with no market return; the FIRE projection compounds a 5%
real return to a 4%-withdrawal target. Each states its basis, but nothing shares one assumptions contract
and neither shows a range (C8).

**G8. Resilience: the contract is only partly built** (§5.7).
No early re-checks for a once-a-day source after a failure (the next attempt is the next daily run); a
confirmed credential loss pauses only bank accounts (IBKR, Flex, Binance and Revolut X select credentials by
`IsActive` alone, so IBKR keeps trying every 15 minutes); the in-process circuit breaker needs about 100
calls in its sampling window, so at one source's volume it never opens and the durable health state is the
breaker that works. Flex codes 1004-1008 stay Transient until IBKR's documentation confirms a better class.
Built in R12 PR 2: the `RateLimited` and `NotReady` classes, `NotBefore`, and the per-source settings record.
Only Monobank and TrueLayer set a `NotBefore` from `Retry-After`; the 429s of Binance, Revolut X and IBKR
are classed RateLimited without one, because their clients do not read the header. The three bank paths stay
in shadow mode: their `NotBefore` is recorded there, and it pauses scheduled bank syncs through
`TransactionSyncCoordinator`.

---

## 7. Updating this document

- A PR that adds or changes money math (a calculation, a label, a scope, a window, a conversion, a quality
  signal) updates [money-semantics.md](money-semantics.md) for what the number means today **and** this
  document for any rule, measure, gap or decision it touches, in the same diff.
- A PR that closes a gap removes it from §6 (and moves the `Today` cell in §3.4 and §2.2).
- A captain decision moves a row from **Open** to **Decided** in §1, with its date.
- The MCP framework primer (`backend/src/FinanceSentry.Mcp/Resources/finance-framework-primer.md`, served as
  `finance-framework://primer`) is the short map of this document; a change to a measure name, the headline
  contract's methods or qualities, a §1 call id or a section it cites updates it in the same diff
  (`FinanceFrameworkPrimerContractTests` fails until it does).
- A new economy adds its row to §5.4 and declares its facets (§5.1), reliability and convertibility (§5.3)
  and failure classes (§5.7) before its adapter is written. Its "Failures and retries, per source" row in §5.7
  is part of the new-source checklist: its signals mapped to each failure class, whether its requests are
  signed or safe to retry, whether failed sign-ins lock the account, its cadence, and its per-source settings
  (`EarlyRechecks` if it runs once a day, `NotReadyDeadline` and `NotReadyRetryAfter` if it can be not ready).
  Then follow the steps in [Adding a new economy](#adding-a-new-economy) (§5.6): register an `IWealthSource`.
- No real balance, amount, token or account identifier belongs in this document.
