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
| C3 | The book headline is the **cash-inclusive brokerage**, with "invested only" shown beneath. The benchmark is **SPY**. | Scope of the book figure and of M3/M9. Today the Book vs SPY figure is invested-only (gap G1). |
| C4 | **Compute in USD; show returns in the person's base currency**, using dated rates for flows and values. | Currency of M3–M5. Flows convert at the flow date's rate, positions at the valuation date's rate (§3.2 rule 5). |
| C5 | The net-worth headline is the **total**, with "of which liquid" beneath. No toggle. | §5.3 totals; presentation pattern P10. |
| C7(i) | **Designs only** from AGPL/GPL projects; **code only** from permissively licensed ones, with attribution. | What may be ported. AGPL/GPL code is never copied; AGPL designs are paraphrased. |
| C8 | **One shared, disclosed projection assumption set**, shown in words next to every projection, as a **range**. | M10; presentation pattern P5. Today's two projections use two sets (gap G7). |
| C9 | **Per-holding checks first**, each threshold cited from a public source. No portfolio-level score. | Presentation pattern P7; the per-holding screen of #824. |

### Open

| Id | Question | State |
|---|---|---|
| C6 | **Steam: whether and how to link.** | **Undecided.** Research comes first (report §5.5, the F1 list); no linking decision is made. The treatment in §5.5 below is the *valuation* rule for when it is decided, not a commitment to link. |
| C1 | IBKR prerequisite steps: enabling Deposits & Withdrawals (and optionally Change in NAV) in the Flex query, and the one-off backfill. | Open, with the provider-data decisions. Without it no IBKR external flow arrives (gap G6). |
| C2 | Contributions vs growth: (a) IBKR and Inzhur flows now and whether to show both MWR and TWR; (b) crypto flows now or later; (c) moving `ibkr-flex` out of shadow alerting. | Open, with the provider-data decisions. |
| C7(ii) | Whether the public repository, unlicensed today, gets a license, and which. | Open; the captain's call. Without a license, default copyright applies. |

---

## 2. Architecture

### 2.1 The umbrella

```
 Layer 2: economies, one adapter each (section 5)
 ┌────────────┬────────────┬────────────┬────────────┬──────────────┬──────────┐
 │ Banks      │ Brokerage  │ Crypto     │ Funds      │ Marketplaces │ Manual   │
 │ Monobank   │ IBKR       │ Binance    │ Inzhur     │ Steam (C6)   │ entries  │
 │ TrueLayer  │            │ Revolut X  │            │              │          │
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

This is the target shape. Today the top (three sources registered as `IWealthSource`, R8 PR 1) and bottom
(dashboard, MCP, briefs) are wired. The measure engine is built for M1-M6 and M9 (3.4); the headline record
(3.6) is built in the backend and MCP and calls M1 for the figures that are changes. The per-source rows are
**designed, not built** (§6).

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
| 1 | Net worth total | `NetWorthSnapshot` (three sleeve columns plus total); live total from `BookFiguresService` (sums the registered `IWealthSource`s: banking, brokerage, crypto) | Sum of sleeves in USD through `CurrencyConverter.ToUsd` at today's rates | `StaleSleeves`, `IsApproximate`, a null `CashTotal`; `FxStatus` (R4): `BookFigures.FxStatus` on the live total, `fx_status` on a stored row (null on a row from before the column) | valuation, M7 | G2 (live total closed), G4, G5 |
| 2 | Dashboard "Change" and its % | `dashboard.computed.ts`; label in `dashboard.component.ts` | (end − start) / start over snapshots: deposits, savings and newly linked accounts all count | none on the figure | M1, later M5 | G3 |
| 3 | Dashboard "Invested change" | `dashboard.computed.ts` | Change in invested value, deposits included; the code comment says it is "not a return" | the comment only | M1 | none: correctly labelled |
| 4 | Book vs SPY (weekly brief, MCP `get_book_performance`) | `BookPerformanceService` | last ÷ first − 1 on invested brokerage value; SPY from adjusted close. **Labelled "change in invested value (includes deposits)"; the `Twr` field names remain.** Beside it, since R1(b), the flow-neutral **holdings-based return** (`holdingsBasedReturn`) against SPY's price return (`spyPriceReturn`, close) | null when history misses the edge tolerance; the holdings-based figure carries a typed reason instead of a number | M1 (not M3), M9 | **G1** |
| 5 | Book drawdown | `BookDrawdownCalculator` | Flow-neutral chained index from holdings snapshots, with split detection | null under two captures | M8 | none: the pattern to reuse |
| 6 | Position returns | `PositionReturnCalculator` | Average-cost walk: unrealized, realized, dividends net of withholding, currency effect | `Coverage` = complete / partial / unavailable | M6 | the vocabulary is local (G5) |
| 7 | 12-month projection tile | `dashboard.computed.ts` | Median monthly savings × 12, no market return | hidden under three complete months | M10 | G7 |
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
  `IsApproximate` reading `FX_TODAY_RATE`; a
  base currency shown on the seed table adds it, and a row dated before today shown in a non-USD base adds it
  too (the display boundary converts at today's rate, not the row's own day's: dated-rate display conversion,
  C4's "dated rates", is a separate queued follow-up). R8's per-source rows will take over this field. A brokerage
  or crypto row stores its USD value converted at sync time, so its status is re-derived from the rate table
  in force when it is read. Display wording is R7.

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
| **Change in value** | End value minus start value, with no adjustment for flows. Includes deposits. This is what "Invested change" and the Book vs SPY figure are today. It is **not a return**. | — |
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
| M1 | Value change | How much more or less do I have? | End − Start. The % is Δ ÷ Start, shown only when the scope did not change, and labelled as including deposits | — | arithmetic | dashboard "Change"; "Invested change"; the Book vs SPY book side. Engine: `ValueChange`, not wired |
| M2 | Contributions vs growth | How much did I put in, and how much did it earn? | growth = ΔValue − net external flows | — | Portfolio Performance categories | engine: `ContributionsAndGrowth`, not wired; no investment flows feed it yet (G6) |
| M3 | Time-weighted return | How did the investments do, regardless of when I added money? The number to compare with a benchmark | Daily chain-linking (3.2 rule 7): Π(1 + r_t) − 1 | GIPS 1.A.35, 2.A.24 | Portfolio Performance `ClientIndex`; the broker's own TWR | engine: `TimeWeightedReturn`, not wired; **no figure is a TWR yet** (the "TWR" names in code are not one; G1) |
| M4 | Money-weighted return | What did *my money* earn, timing included? | XIRR on an actual/365 day count for windows of one year or more; Modified Dietz, not annualised, for shorter windows | GIPS 2.A.29, 2.A.12 | Excel XIRR / ExcelFinancialFunctions; hledger `roi` | engine: `MoneyWeightedReturn` (XIRR ported from ExcelFinancialFunctions), not wired |
| M5 | Net-worth bridge | Why did my net worth change? | 3.3 | — | Portfolio Performance categories | engine: `NetWorthBridge`, not wired |
| M6 | Gain vs cost | How much am I up on what I hold? | unrealized = value − average cost; realized = proceeds − average cost of what was sold; plus income and currency effect | standard average cost | Ghostfolio, Paisa | per position (`PositionReturnCalculator`); engine: `GainVsCost`, not wired; #982 asks for it at header level |
| M7 | Allocation | What is my money in? | weight = value ÷ total by asset class, currency, source, convertibility. Liabilities shown separately; weights over gross assets | — | arithmetic | partly (sleeves) |
| M8 | Risk | How bumpy is it, and how deep are the falls? | Volatility = standard deviation of monthly TWR returns × √12 (the "3-year" figure needs 36 months). Max drawdown on a flow-neutral index. Downside deviation over *all* observations | GIPS 4.A.1.j; empyrical definitions | empyrical-reloaded, quantstats | drawdown only |
| M9 | Benchmark-relative | Did I beat the simple alternative? | Excess = book TWR − benchmark total return over the identical window and periodicity. Benchmark is SPY (C3) | GIPS 2.A.18 | empyrical | Book vs SPY compares a value change with SPY's adjusted close (G1). Engine: `ExcessReturn`, not wired |
| M10 | Projection | Where am I heading? | A savings path plus a stated return assumption, one shared assumption set, shown as a range (C8) | FINRA 2210(d)(1)(F) / 2214 pattern; DOL lifetime income illustration | — | tile + FIRE, two assumption sets (G7) |

Choices the engine fixes once, so they stop being re-decided:

- **Day count:** actual/365 (what Excel, LibreOffice and ExcelFinancialFunctions use).
- **Annualising:** never for windows under one year (GIPS 2.A.12).
- **Failures are typed.** An XIRR with no sign change, no convergence or a zero base returns a reason, not a
  number and not an exception. Never return `0` on bad input.
- **Return base:** a window starting at a value of zero (a new account) has no percent. Show the money
  figures with reason `ZERO_BASE`.

**The engine.** `backend/src/FinanceSentry.Core/Measures/` holds M1-M6 and M9 as pure static calculators,
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
for the figures that are changes (net-worth history's window change, the book side of `get_book_performance`);
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
| Today's Book vs SPY book side | (19,800 − 10,000) ÷ 10,000 | **+98%**: a value change, not a TWR |

TWR and MWR differ because twice as much money was invested during the losing half as during the winning
half. TWR ignores that, so it is the number to compare with SPY; MWR is the honest answer to "what did my
money earn". Both are correct; they answer different questions. This example is golden fixture #1 for the
measure engine (`MeasureEngineGoldenFixtureTests`). Its XIRR there is −1.328%, not the idealised −1.332%:
the real dates put the deposit 181 days after the first flow (exponent 181/365), not exactly half a year.

### 3.6 The headline contract

Every top-line figure carries the same record: the dashboard hero, the accounts and investments headers,
every MCP tool that returns a total or a return, and the briefs. **Status: built in the backend and MCP
(R6, first PR); the badges and reason sheet (P9) are the second.** The shape is paraphrased from
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
quality is the worst its reasons imply and a figure with no amount is UNAVAILABLE. It is an additive field
beside the existing ones:

| Surface | Field | What it describes |
|---|---|---|
| Dashboard (`GET /dashboard/aggregated`) | `netWorthHeadline`, `cashHeadline`, `investedHeadline` | the hero (`umbrella`) and the C5 split lines (`sleeve:cash`, `sleeve:invested`; `NO_SPLIT` when withheld) |
| Wealth summary | `headline` | the accounts header total; `source:<provider>` / `sleeve:<category>` when filtered |
| Crypto and brokerage holdings | `headline` | the investments headers' sleeve totals |
| Net-worth history | `changeHeadline` | first to last point of the window, M1 (`SCOPE_CHANGED` from R3 withholds the percent) |
| `get_portfolio_snapshot` | `headlines` (`totalValue`, `investedValue`, `cash`, `unrealizedPnl`) | the book totals; `unrealizedPnl` is `GAIN_VS_COST` with `basisStatus` and `coverage` |
| `get_net_worth_history` | `headline` per point | each day's total |
| `get_book_performance` | `book` per period | the book side, M1 over `invested_brokerage` (`SHORT_HISTORY` or `ZERO_BASE` when there is no percent) |
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
| Unknown currency converted 1:1 (`CurrencyConverter.Convert` → `UNKNOWN_1TO1`) | DEGRADED + `FX_UNKNOWN_1TO1` (reason code live, R4) |
| Historical FX replaced by today's rate (`TODAY_RATE`) or a seed-table rate (`SEED_TABLE`) | ESTIMATED + `FX_TODAY_RATE` (reason code live, R4) |
| `HasStaleSleeves` on FIRE | DEGRADED + `STALE_SOURCE` |
| "Book N/A" | UNAVAILABLE + `SHORT_HISTORY` or `NO_SPLIT` |
| A measure's failure (`MeasureFailure`) on a change | the amount stands, the percent is null: `ZERO_BASE`, `NEGATIVE_BASE`, `SCOPE_CHANGED`, `SHORT_HISTORY` (a missing end: UNAVAILABLE), `NOT_MEASURABLE` |
| (new) | `UNKNOWN_FLOW`, `UNMATCHED_TRANSFER`, `NON_CASH_INCLUDED`, `PRICE_CARRIED_FORWARD`, `RESIDUAL_ABOVE_TOLERANCE` |

Every row above except FIRE is mapped in `HeadlineQualityMapper` (tests: `HeadlineQualityMapperTests`).
A live total's stale source is a sleeve whose read failed (`BookFigures.StaleSources`), a provider past its
freshness window, or an institution whose sync status is stale, failed or needs re-authorisation. The
`(new)` codes have their quality fixed (ESTIMATED for `UNKNOWN_FLOW`, `UNMATCHED_TRANSFER`,
`PRICE_CARRIED_FORWARD`; DEGRADED for `NON_CASH_INCLUDED`, `RESIDUAL_ABOVE_TOLERANCE`) but no source raises
them until flows (G6) and the source registry (R8) land.

**Not yet carrying the record** (R6's remaining work, with P9): the FIRE projection and its `HasStaleSleeves`;
the briefs (`PerformanceBriefComposer`, the FIRE brief); the MCP tools whose figures are per thesis, per
recommendation or per flow rather than a book total or return (`get_benchmark_track_record`,
`get_thesis_performance`, `get_track_record`, postmortems, `get_cashflow_report`, budgets, the family
clearing statement) and per-row figures (positions, accounts, tax lots); a net-worth change headline on
`get_net_worth_history` (the tool returns a bare list); scope-change detection on the book side of
`get_book_performance` (it always measures with no scope change); the holdings-based return on
`get_book_performance` (`holdingsBasedReturn`; the method label for a chained, dividend-excluded holdings price
return is the next PR's choice); and dated-rate display conversion.
**P9** then renders `quality` as a badge on the dashboard hero, the accounts and investments headers and the
split lines, with a reason sheet whose words come from the `ERROR_MESSAGES` registry, and moves the
dashboard's own "Change" percentage (`dashboard.computed.ts`) onto `changeHeadline` (G3).

**Display rules:**

| Rule | What it says |
|---|---|
| D1 | One label, one numerator. "Change" is always M1, "Growth" is always M2, and "Return" always names its method in a secondary label. An amount and a percent on the same line share the same numerator. |
| D2 | Never hide a sign. Show + and −, and normalise −0 to 0. |
| D3 | All-time on a holding means gain vs cost (M6). A bounded period means value change over the start value (M1) or a named return (M3/M4). Never mix them on one line. |
| D4 | A figure with mixed scope is never labelled TWR. A percentage appears only when coverage is coherent: the whole scope is measured, or the unmeasured part is named. |
| D5 | Missing cost basis shows "N/A" with its reason, never 0%. |
| D6 | No annualising under one year (GIPS 2.A.12). Shorter windows show the cumulative figure. |
| D7 | Unavailable, loading and zero look different: a skeleton while loading, "—" with a reason when unavailable, "0" only for a real zero. |
| D8 | An ESTIMATED or DEGRADED figure carries a badge, and the reason is one tap away. |
| D9 | Big headline numbers may use compact notation on phones ("$1.2M"), with the full figure one tap away, through `MoneyUtils` / the `money` pipe. Never compact a figure the person reconciles against a statement. |

### 3.7 Presentation patterns

A big number becomes meaning through a small set of patterns. Each needs its measure first; none is built
yet except where noted.

| Id | Pattern | What the person sees | Needs |
|---|---|---|---|
| P1 | Value-first rows | Each row's main figure is its value; quantity and price are secondary | valuation |
| P2 | Growth of 10,000 | What 10,000 invested at the start would be worth now, book vs benchmark | M3, M9 |
| P3 | Contributions vs growth | "You put in X; it grew by Y" | M2 |
| P4 | Bridge waterfall | Start → savings → investment return → FX → linked → end | M5 |
| P5 | Monthly-income lens | A large total also shown as the monthly income it could pay | M10, C8 |
| P6 | 1 / 5 / 10-year table | Average annual total return per standard window, book vs benchmark | M3, long history |
| P7 | Checklist score | A few yes/no checks per holding, drawn as a shape; ideas only, thresholds cited (C9) | cited thresholds; fits #824 |
| P8 | Timing gap | "Your investments returned A; your money earned B; the difference is timing" | M3, M4 |
| P9 | Quality badges | "Estimated", "Stale" or "Partial" with a reason sheet | headline contract |
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
`BookFiguresService` sums whatever is registered. The contract below is still the target: today a source
returns `WealthSourceReading` (USD cash, invested positions, worst FX status), not the full
`SourceSnapshot`, and has no `asOf`, native units, reliability or events.

### 5.1 What every source supplies

A source declares which facets it can supply; the engine computes only the measures those facets support.

| Facet | Contents | Required | Enables | Today's equivalent |
|---|---|---|---|---|
| Identity | source id, economy kind, native unit(s) | yes | — | provider name strings |
| Holdings | asset, quantity, native unit, exit price, price time, price source | yes | value, M1, M7 | `BankingAccountSummary`, `BrokerageHoldingSummary`, `CryptoHoldingSummary` |
| Cash and liabilities | cash balances; debts, signed as in money-semantics §1 | when the economy has them | liquid net worth, the cash/invested split | the cash split in `BankingWealthSource`, `BrokerageWealthSource`, `CryptoWealthSource` |
| Flows | dated movements with a counterparty reference and transaction id | no | M2, M3, M4, M5 | a landing table for IBKR and Inzhur with no reader; none for crypto (G6) |
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
public sealed record SourceEvent(
    DateOnly Date, string ProviderType, decimal Amount, string Unit,
    string? CounterpartyRef, string? TxRef);

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
| IBKR | Brokerage | positions + cash | landing table exists; the "Deposits & Withdrawals" type is not in the Flex query (C1) | L1 | CASH | flows not ingested (G6) |
| Inzhur | Fund | units (`InzhurHoldingsMapper`) | deposits land in the same table | L2 (published unit price) | CASH or LOCKED, **not verified** (redemption terms) | wrap; check redemption terms |
| Binance, Revolut X | CryptoVenue | balances, venue fiat | not ingested | L1 for liquid pairs | CASH; LOCKED for lock-up products | fiat flows buildable; on-chain flows need research |
| Self-custody wallets | ChainWallet | — | — | — | — | not built; needs research |
| Steam | Marketplace | wallet + inventory | wallet top-ups, market sales: unverified | L1–L3 per item | IN_ECONOMY; LOCKED under a trade hold | **undecided (C6)** |
| Manual (property, pensions, private holdings) | Manual | value the person enters | entered by the person | L3 | as declared | not built; only if wanted |

### 5.5 Steam: the valuation rule, and what is undecided

**Linking Steam is undecided (C6): research comes first.** What follows is the valuation treatment that
applies *if and when* it is linked, so the rule exists before the decision. The verified facts, the quotes
and the list of what is not yet verified are in report §5.5 and §12.4; none of it is a commitment.

| Holding | Value | Reliability | Convertibility |
|---|---|---|---|
| Wallet balance | balance, in its currency | L1 | IN_ECONOMY |
| Market items | exit price (5.3 step 2), on the marketplace's own market | L1 with recent sales; L2 thin or similar; L3 otherwise | IN_ECONOMY; LOCKED under a trade hold |

- **Totals.** Included in total net worth, excluded from liquid net worth.
- **Classification is the question that matters most.** Today a card payment to a marketplace is spending at
  the bank layer. Once the marketplace is linked, a wallet top-up is an *internal* transfer at the umbrella
  scope and an item purchase is an asset purchase, while a game purchase stays spending. Linking would
  therefore change "monthly outflow" for anyone who buys items.
- **Not verified, and part of the research:** the inventory and price endpoints (undocumented), their rate
  limits, whether hidden and trade-held items appear, whether the terms allow a third party to aggregate
  inventory value, and how third-party cash-out marketplaces are treated.

### 5.6 From three sleeves to a registry (four PRs)

Today a fourth economy would need a new column on `net_worth_snapshots`, a new constructor parameter on
`BookFiguresService`, a new name inside the `StaleSleeves` string, and a change in every consumer.

1. **Wrap. Done.** The three readers (`IBankingAccountsReader`, `IBrokerageHoldingsReader`,
   `ICryptoHoldingsReader`) are three `IWealthSource` registrations (`AddBookFigures`, which registers them in
   the order positions have always had: crypto, brokerage, banking). `BookFiguresService` loops over the
   registered sources; its consumers did not change. A source id is the name `StaleSources` has always
   carried (`banking`, `brokerage`, `crypto`), and a kind with no legacy slice (`EconomyKind`) counts in
   `CashUsd` and the totals only. `BookFiguresCharacterizationTests` pins the figures, and
   `BookFiguresWealthSourceTests` that a fourth registered source is summed.
2. **One row per source. Remaining.** A child table (for example `net_worth_snapshot_sources`: snapshot id, source id,
   kind, total, cash, invested, health, reasons), written by the snapshot job alongside the existing
   columns. `WealthDbContext` owns it, so there is no cross-module migration ordering.
3. **Read per source. Remaining.** History, the dashboard and MCP read the per-source rows. `StaleSleeves` gives way
   to per-source health and reasons.
4. **Freeze the old columns. Remaining.** Keep `BankingTotal`, `BrokerageTotal`, `CryptoTotal` and `StaleSleeves` for
   history, and stop extending them. The first new economy comes after this step.

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
| **Transient** | 5xx, 408, network errors, timeouts; IBKR's "backend down" 401 | Exponential backoff with jitter, safe methods (GET) only; 2 retries by default | The next scheduled run; a once-a-day source also looks again earlier (early re-checks below) | Nobody while Degraded. The outage alert once Failing (at least 3 failures and 2 h without success). Dormant after 7 days |
| **RateLimited** | 429; Monobank's rate-limit code; Flex 1018 | Wait as long as `Retry-After` asks if that fits the call's time budget; otherwise stop at once | No scheduled call before the time the provider named (`ConnectionHealth.NotBefore`, from `Retry-After`, as named; a bank sync is skipped, a sweep leaves the credential alone) | Counts like Transient; never a credential signal. A 429 that persists across runs reaches Failing like any outage |
| **NotReady** | Flex 1003, "statement is not available" | Poll while the provider says it is still generating (Flex: the retries of 1003 and 1019 inside a run) | Flex reschedules itself sooner (every 2 h, until the next daily run) instead of waiting for it | Not counted until the last success (the connection's creation, before the first one) is older than the source's NotReady deadline (48 h for Flex); then like Transient. A source with no deadline counts it at once |
| **Credential, definitive** | TrueLayer `invalid_grant` with a consent marker; Flex 1012–1015; a stored key that cannot be decrypted | Never retried | Scheduled calls pause, except one probe a day, until the person reconnects or a probe succeeds | The reconnect alert at once (ActionRequired) |
| **Credential, suspect** | 401/403 without proof: Monobank 403, IBKR and Revolut X 401/403, Flex 1020 | Never retried (repeated failed sign-ins are what trigger account lockouts) | Later runs try again with a fresh session where the source has one (a once-a-day source: its early re-checks, so the confirming sighting comes the same day). A sighting at least 30 min after the first confirms it. Simultaneous failures across one bank's connections read as an outage | The reconnect alert once confirmed |
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

**Failures and retries, per source** (each new source fills in this table, and a new source's job declares its
settings the way Flex does in `BrokerageSyncModule`):

| Source | In the call | Across runs | Classification | Settings (`ConnectionHealth:Sources:<source>`) | Policy-driven alerts |
|---|---|---|---|---|---|
| IBKR | No retries (signed); drops its cached session token on a 401/403 | Every 15 min; ActionRequired pauses it, one probe a day | 401/403 suspect; "backend down" 401 transient | shared defaults | Yes |
| IBKR Flex | No retries; limiter 1 call/s and 10/min; polls while generating | Daily 06:00; early re-checks after 1 h and 4 h; NotReady retried every 2 h until the next run | 1012–1015 definitive; 1020 suspect; 1003 NotReady; 1018 RateLimited; 1001, 1004–1009, 1019, 1021 transient | `EarlyRechecks` 1 h, 4 h; `NotReadyDeadline` 48 h; `NotReadyRetryAfter` 2 h | Shadow (`ibkr-flex` not yet in `ActiveProviders`) |
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

**G1. "Book vs SPY" is a change in invested value, not a time-weighted return.**
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
money-semantics §10. Later step: a true TWR once dated external flows land (G6, R1(c)).

**G2. Currency fallbacks are silent at the headline.** *Closed for the live total and stored snapshot rows (R4); per-source status waits for R8.*
`CurrencyConverter.ToUsd` still returns an unknown currency's amount unchanged, but the headline readers now
use `Convert`, so `BookFigures`, the snapshot job and net-worth history say when a seed-table, today's-rate
or 1:1 conversion touched a figure (`FX_TODAY_RATE`, `FX_UNKNOWN_1TO1`). A snapshot row records the worst
status of its sources (`fx_status`; R8's per-source rows will take over), and the amount-only `ToUsd` callers
outside the headline paths are unchanged. money-semantics §9 accepts the approximations.

**G3. The dashboard "Change" percentage counts newly linked accounts.**
(end − start) ÷ start over snapshots. Savings legitimately belong in a value change and the label is honest
about that (M1), but linking an existing account is not a change in wealth, yet shows as one, with a
percentage (rule 3 of §3.2; reason `SCOPE_CHANGED`).

**G4. The sleeves are hard-coded.** *Partly closed (R8 PR 1): the live book sums registered sources; the stored
snapshot and its consumers are still per sleeve.*
Three total columns on `net_worth_snapshots` and staleness as a comma-separated string of sleeve names. A new
economy would still be a schema change plus a change in every snapshot consumer until the per-source rows
(§5.6, PRs 2-4) land.

**G5. Quality signals are scattered and untyped.** *Closed for the backend headline figures and the MCP
totals (R6, first PR); the frontend still reads the untyped fields until P9.*
`StaleSleeves` (string), `IsApproximate` (bool), `CashTotal` null, `Coverage` (string), `HasStaleSleeves`
(FIRE flag), "Book N/A" (null fields), FX substitution. The headline figures and the MCP tools that return a
total or a return now carry the typed record beside these (§3.6), so MCP reads one vocabulary; the untyped
fields stay for current consumers. The FIRE flag, the briefs and the per-thesis and flow tools are not yet
covered (§3.6, "Not yet carrying the record").

**G6. Investment flows land but nothing reads them.**
IBKR and Inzhur cash rows land in `brokerage_sync."BrokerageCashTransactions"`; only income types are read.
No IBKR deposits or withdrawals arrive, because the Flex query does not include that type (C1). Crypto
venues store no flows. Without flows, M2–M5 cannot be computed for any investment scope, so G1 cannot be
fixed properly either.

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
- A new economy adds its row to §5.4 and declares its facets (§5.1), reliability and convertibility (§5.3)
  and failure classes (§5.7) before its adapter is written. Its "Failures and retries, per source" row in §5.7
  is part of the new-source checklist: its signals mapped to each failure class, whether its requests are
  signed or safe to retry, whether failed sign-ins lock the account, its cadence, and its per-source settings
  (`EarlyRechecks` if it runs once a day, `NotReadyDeadline` and `NotReadyRetryAfter` if it can be not ready).
- No real balance, amount, token or account identifier belongs in this document.
