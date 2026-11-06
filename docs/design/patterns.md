# Money design patterns

The money-specific design rules and screen patterns of Finance Sentry: how a figure is set,
formatted and coloured, how a change of value reads, which fixed colours carry a meaning, and which
reference app each finance screen is judged against.

The design system itself is lifekit-wide and lives in
[lifekit-common](https://github.com/lifekit-hq/lifekit-common): templates, rhythm, tokens and
components for every app. Its `DESIGN.md` is the look, and
[`docs/design/guardrails.md`](https://github.com/lifekit-hq/lifekit-common/blob/main/docs/design/guardrails.md)
is the behaviour policy with the check behind each rule. Anything about money belongs here instead
(issue #1339). This page carries every money-specific rule that `DESIGN.md`, `guardrails.md` and
`patterns.md` in lifekit-common held when it was written, each preserved or improved, with the
check that holds it where one exists. The library pages are unchanged by this move; trimming them
is a separate change in lifekit-common.

Where this page and the library disagree, the library wins on layout and look (templates, tokens,
components), and this page wins on what a figure means and how it reads. Where the app differs
from a rule today, the difference is named in [Finance Sentry as built](#finance-sentry-as-built),
not hidden.

Money calculation lives elsewhere: [money-semantics.md](../money-semantics.md) is the source of
truth for what a number means, and [finance-framework.md](../finance-framework.md) for how any
number is derived. This page is display only. The frontend gates that apply to every amount
(`MoneyUtils` / `money` pipe, `inputmode` on numeric fields) are in
[frontend-rules.md](../claude/frontend-rules.md).

## Contents

- [Principles](#principles)
- [Figures](#figures)
- [Money formatting](#money-formatting)
- [Deltas](#deltas)
- [Colour with a fixed meaning](#colour-with-a-fixed-meaning)
- [Lists, tables and stat cards](#lists-tables-and-stat-cards)
- [Charts](#charts)
- [Rules and their checks](#rules-and-their-checks)
- [Reference registry](#reference-registry)
- [Picks and named gaps](#picks-and-named-gaps)
- [Screen patterns](#screen-patterns)
- [Finance Sentry as built](#finance-sentry-as-built)

## Principles

- The interface is a precision instrument for reading money: every pixel serves reading a balance,
  a transaction or a status correctly at a glance. Nothing decorates, and density serves daily
  checking on desktop and phone, in light and dark. (lifekit `DESIGN.md`, "The Instrument".)
- Money must be read correctly at a glance. Decoration never competes with numbers.
- One primary number per screen, with detail behind
  [progressive disclosure](https://www.nngroup.com/articles/progressive-disclosure/) (the Revolut
  bar, rule X3). Which number is primary is a product decision, so review holds it.
- The numbers in a finance app are refreshed daily over cached data, so cached data stays on screen
  during a refresh: offline beats error, and error beats empty. (lifekit guardrails area 1: Carbon
  is silent on refreshing over cached data, "the daily case in a finance app".)

## Figures

- Money is set in the text face (IBM Plex Sans), never in monospace. Plex Mono is for code only.
- Tabular figures everywhere, so amount columns align even where a component forgets
  `tabular-nums`. Plex figures are tabular by default.
- A stat value is a **headline** (600, 1.5rem, line height 1.25): stat values and section titles
  share that role.
- A figure's label is sentence case, 12px minimum: no uppercase or tracked micro-labels, and no
  decorative accent on the figure.

## Money formatting

All money and number display goes through one formatter (rule $1). The standard is Unicode CLDR
through ECMA-402 `Intl.NumberFormat` and `Intl.DateTimeFormat`, in the app's `LOCALE_ID`: CLDR
decides symbol placement, separators, the minus sign and compact notation.

- **One formatter.** The library holds `formatMoney` and `formatNumber` in
  `@lifekit-hq/core/format`, with the `cmnMoney` and `cmnNumber` pipes over them in
  `@lifekit-hq/ui`. Finance Sentry formats through `MoneyUtils.format` and the `money` pipe
  (`frontend/src/app/shared/utils/money.utils.ts`); see
  [Finance Sentry as built](#finance-sentry-as-built). A displayed amount never goes through the
  Angular `currency` pipe, `DecimalPipe` plus a hand-appended currency code, or a local
  `Intl.NumberFormat` / `toLocaleString` with `style: 'currency'`.
- **Symbol first.** `currencyDisplay: 'narrowSymbol'`: "₴18,981.48", not "UAH 18,981.48"
  (ruled 2026-10-01).
- **The ambiguous case shows the ISO code.** A screen that shows two currencies sharing a narrow
  symbol (USD and CAD) shows the ISO code (Polaris's rule for the ambiguous case).
- **Deltas carry a sign**, covered in [Deltas](#deltas).
- **A base-currency equivalent** is shown muted on its own second line (`~ $1,235`), not mixed
  into the native amount.
- **Numeric inputs** declare `inputmode` (`decimal` for money, `numeric` for whole counts) so a
  phone shows the right keyboard.
- **Push text is amount-free.** A notification never carries an amount in its text.

## Deltas

- A delta uses `signDisplay: 'exceptZero'`, plus an arrow, plus the fixed `gain` or `loss` colour.
  Colour is never the only signal (WCAG 1.4.1), so a change always also carries its sign or an
  arrow (rules C4 and $2).
- The delta text is coloured, not the whole chart or the whole screen. A sparkline that shows one
  gain or loss, coloured by its trend, uses the `gain` and `loss` tokens.
- Green and red mean "went up" and "went down" (`gain`, `loss`), and the matching status. They
  are never a category.
- Not copied from Robinhood: the whole-screen red/green repaint. A 0.02% dip gets the same red as a
  20% loss, and the red screen is tied to panic selling ([Pratt IxD
  critique](https://ixd.prattsi.org/2025/02/design-critique-robinhood-ios-app/)), a trading nudge
  the registry rejects for a hold-and-reason tool.

## Colour with a fixed meaning

A few colours mean one thing in every app and under every seed, so the token engine does not
derive them and no seed stylesheet sets them. They are never seed-derived (rule C2, ruled
2026-10-07), and colour is never the only signal.

| Role        | Tokens                                                                              | Rule                                                                                                            |
| ----------- | ----------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------- |
| Delta       | `delta-up`, `delta-down`                                                            | Deprecated. Aliases of `gain` and `loss`; use those. They go in the next major.                                 |
| Fixed       | `asset-equity`, `asset-crypto`, `asset-cash`, `gain`, `loss`, `flow-in`, `flow-out` | A colour that carries a meaning, so no seed changes it.                                                         |
| Fixed fills | `flow-in-fill`, `flow-out-fill`                                                     | Cash flow as a mark (a bar, a legend dot), a lightness gap apart. `flow-in` and `flow-out` stay the text grade. |
| Categories  | `category-1`…`category-14`, `category-other`                                        | One fixed colour per category, the same in every theme, range and screen.                                       |

- Equity is `asset-equity` (the petrol of the original Finance Sentry), crypto is `asset-crypto`
  (amber), cash is `asset-cash` (green), a change of value is `gain` / `loss`, and cash flow is
  `flow-in` / `flow-out` (the same colours as gain and loss, with `flow-in-fill` and
  `flow-out-fill` for the marks). A categorical palette (`category-1`…`category-14`,
  `category-other`) completes the set.
- Light and dark share the hue; only lightness moves, and only as far as contrast needs: 3:1 for a
  mark (a donut slice, a legend dot), 4.5:1 for `gain` and `loss` as text, on the page, card and
  raised surfaces (`surface-bg`, `surface-card`, `surface-raised`; `surface-hover` is a transient
  state and is not guaranteed). `test/fixed-colours.spec.mjs` in lifekit-common holds this.
- Use them for the thing they name and nothing else. `asset-cash` is a green, so it is told apart
  from `gain` by its label, as colour is never the only signal.
- **Cash flow has two grades.** `flow-in` and `flow-out` are text (the colours of `gain` and
  `loss`, 4.5:1 on a surface). They are the same lightness, so as bars they blur for red-green
  colour blindness. A bar, a legend dot or a slice takes `flow-in-fill` and `flow-out-fill`: the
  same hues, at least 1.4:1 apart in lightness in light and dark (the inflow the lighter), each 3:1
  on a card. Never set text in a fill token; never fill a bar with the text pair.
- **Categories are fixed, not ranked.** A screen that colours spending categories maps each
  category key onto a `category-N` token once and keeps the mapping, so a category wears one colour
  in every theme, range and screen; it never takes the colour of whatever ranks where, and it never
  takes a seed-derived chart series (those shift with the accent). `category-other` is the neutral
  for a folded "other" or an uncategorised slice. The hues skip the loss, gain and asset arcs, each
  colour is 3:1 on a card and no two are closer than 0.08 in OKLab. Indices are append-only. With
  14 colours the later ones are mostly told apart by lightness inside a family, so a chart with
  that many slices also labels them. A chart of a few categories uses the first few.
- **Delta-only colour.** A chart's categories use the series tokens. The engine walks series 2-8
  around the colour wheel from the seed and skips the red and green arcs, so a category never reads
  as a gain or a loss. `@lifekit-hq/charts-core` defaults to that order (`CATEGORICAL_STEPS`).
  Categories that must keep one colour across screens and ranges (spending categories) take the
  fixed `category-N` palette instead.
- Finance Sentry's seed is Petrol `#175a6d`, default intensity Quiet (0.12). A user can pick
  another colour and intensity in Settings > Appearance, and the fixed colours above do not move
  with it.

## Lists, tables and stat cards

- **Data table / list rows:** sentence-case headers, hairline dividers, right-aligned tabular
  amounts (rule T1). A row's action is a quiet trailing icon (`cmn-list-item-row` `trailingIcon`),
  not a bordered text button repeated on three or more rows.
- **An empty cell** shows "-" with a screen-reader label (rule T2).
- **Below 600px**, a table renders as list rows in a `cmn-list-section` with continuous paging
  (`lk-infinite-list`) and no pager. A day of transactions is one section, and its
  `cmnListSectionAside` slot carries the day's total.
- **Stat card:** the balance is the content. Value in the text face with tabular figures; no
  uppercase label, no decorative accent. The hero-metric layout is deliberately kept for this
  product. A stat card's delta carries an icon, and a money delta a sign (rule C4).
- **Period reports** are a month stepper, a few headline tiles and a table, not a new screen.

## Charts

The library adopts Carbon data visualization. These are the parts that make a chart a money chart.

- Bars and areas start at zero (a line may not); gaps are never interpolated (rule Ch2).
- **Scrub-to-read** on touch: hold a finger on the chart to read a past point, the headline number
  follows and snaps back on release; arrow keys do the same (rule Ch3). Phone-first finance has no
  hover.
- **Fixed asset and `category-N` colours** replace Carbon's sequence, keeping its count of 14
  (ruled 2026-10-07), so a category keeps one colour across screens.
- Every chart has an accessible name stating the trend, plus a data-table view (rule Ch1).

## Rules and their checks

The money-specific rules, with the label the library gives each. Checks live in lifekit-common
unless noted. **exists** is a gate that predates the policy, **enforced** a check that landed with
it, **review-only** a rule no check can decide.

| #   | Rule                                                                                | Enforcement                                                                                                    |
| --- | ----------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------- |
| T1  | Amounts are right-aligned with tabular figures                                      | **exists**: the `AlignedColumns` story; Plex figures are tabular by default                                    |
| T2  | An empty cell shows "-"                                                             | **enforced**: the data-table "empty cells" specs and the `EmptyCells` story                                    |
| C2  | Fixed-meaning colours are never seed-derived                                        | **exists**: `fixed-colours.spec.mjs`                                                                           |
| C4  | Colour is never the only signal                                                     | **review-only**: axe cannot see meaning. Stat-card deltas carry an icon, and money deltas a sign ($2)          |
| Ch1 | Every chart has an accessible name stating the trend, plus a data-table view        | **enforced**: each chart spec and a `DataTable` story per chart                                                |
| Ch2 | Bars and areas start at zero; gaps are never interpolated                           | **enforced**: charts-core specs on the bar, area and line builders                                             |
| Ch3 | Scrub works by touch and by arrow keys                                              | **enforced**: charts-core `scrub` specs and the line and area chart specs                                      |
| $1  | All money and number display goes through one library formatter                     | **enforced**: `formatMoney`, `cmnMoney` and `cmnNumber`, plus consumer-lint `lifekit/no-local-currency-format` |
| $2  | Deltas carry a sign; an ISO code appears when same-symbol currencies share a screen | **enforced**: `money.spec.ts` across en-IE and uk-UA                                                           |
| X3  | One primary number per screen; detail behind disclosure                             | **review-only**: which number is primary is a product decision                                                 |

`lifekit/no-local-currency-format` flags `Intl.NumberFormat` and `toLocaleString` with
`style: 'currency'`, and the Angular `currency` pipe. In this repo the matching gate is
`MoneyUtils` / the `money` pipe, per [frontend-rules.md](../claude/frontend-rules.md).

## Reference registry

The app list is pinned to the registry below (1-12 plus 9a). Binance, OKX and Bitget were skipped
on 2026-10-07. Copilot Money is not in the registry. It is the clearest public example of upcoming
recurring charges (dashboard "Upcoming" recurrings, [Copilot Money, Dashboard
tab](https://intercom.help/copilotmoney/en/articles/6045480-dashboard-tab)), but Finance Sentry's
subscriptions page already covers that, so it stays an outside example rather than a registry
entry. Capitellas and Dapper Dividends are cited for the digest format only.

| #   | Reference                       | Best single borrow                                                                                                            | Not borrowed                                                          |
| --- | ------------------------------- | ----------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------- |
| 1   | Taras Guk                       | Per-share layout: verdict header, one-line thesis, perf strip, benchmarked chart with period tabs, 2-column fundamentals      | His opaque badge                                                      |
| 2   | Simply Wall St                  | A plain-English "what this means" line per section                                                                            | The Snowflake radar, the "6/6 passed" checklist                       |
| 3   | Finviz                          | Treemap heatmap of the book (reference only for now)                                                                          | Coverage-maximalism                                                   |
| 4   | Yahoo Finance                   | Per-ticker IA as a completeness checklist: header, chart, key stats, news, compare mode                                       | Ad density                                                            |
| 5   | Revolut                         | One clear number per screen; predicted month-end spend                                                                        | Shallow-by-design                                                     |
| 6   | Seeking Alpha                   | Factor grades kept separate, never averaged; a Bull/Bear two-column layout                                                    | The Quant Rating composite, the paywall                               |
| 7   | Webull                          | Chart UX: timeframe switch, benchmark overlay, opt-in technicals                                                              | Fifteen-indicator overload                                            |
| 8   | Robinhood                       | Positions row (day change plus sparkline); stock-page order (chart, position, stats); scrub-to-read; "why it's moving" digest | Gamified nudges, the whole-UI red/green repaint                       |
| 9   | Interactive Brokers             | Notifications scoped to held positions; statements as period reports; the period picker                                       | Mail-blast cadence, legalese                                          |
| 9a  | Trading Central ETF Trade Ideas | Idea-card shape: instrument, call, what would change it, horizon                                                              | Pattern signals as an input, sponsored placement                      |
| 10  | StockAnalysis.com               | Fair value with visible assumptions and a sensitivity table; compare table; terse daily bullets                               | Coverage-maximalism                                                   |
| 11  | Zacks                           | Per-style letter grades; earnings-surprise history                                                                            | The composite Rank                                                    |
| 12  | monobank                        | Fast transaction-list filters (period, category, search)                                                                      | Bank-only surfaces, gamification, statement export (product boundary) |

Taras Guk's screens are paywalled (Telegram @tarasguk, tarasguk.com), so the registry entry is the
primary record for #1.

Two registry borrows lose to the product boundary (a Finance Sentry app is not a trading venue, and
where the source already does the job well it links to it): monobank statement export, and
Robinhood-style percent-move price alerts, which IBKR and Binance already send.

The captain's rule (2026-10-07): keep the reference resources and compare with them as we go. So
every pattern below names its reference with a **Compare against** link, the lifekit-common piece
that owns it (or "gap"), and its consumer. A review of a screen or component built from a pattern
checks back against that source, not against memory of it.

## Picks and named gaps

The captain's picks (2026-10-07, reference round 1) and what each means for the library.

| #   | Pick          | Pattern it chose                                                                                                            | Owner in lifekit-common                              | Consumer                               |
| --- | ------------- | --------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------- | -------------------------------------- |
| 1   | 1-charts-a    | [Chart period and scrub](#chart-period-and-scrub): one-row period control, scrub-to-read, delta-only colour                 | `cmn-segmented`, charts-core scrub (gap)             | dashboard, events, dossier             |
| 2   | 2-dashboard-a | [Hero card](#hero-card): number and delta, then chart, then the period control under it                                     | `cmn-card`, `cmn-stat-card`, chart pieces (gap)      | Finance Sentry dashboard               |
| 3   | 3-holdings-a  | [Positions row](#positions-row): day change per row, 1M sparkline later                                                     | sparkline mode (gap)                                 | Finance Sentry holdings                |
| 4   | 4-dossier-a   | [Entity page order](#entity-page-order): Robinhood/Taras order                                                              | `cmn-card`, `cmn-description-list`, stat strip (gap) | Finance Sentry asset dossier (#824)    |
| 5   | 5-verdict-a   | [Verdict idea card](#verdict-idea-card): STRONG BUY / BUY / WAIT / AVOID word, four raw axis facts, what would change it    | verdict badge and axis strip (gap)                   | dossier header, ranked universe (#726) |
| 6   | 6-notify-a    | [Scoped notifications](#scoped-notifications): held names on by default, watchlist opt-in, consensus and timing on earnings | `cmn-alert-item`, `cmn-chip`                         | Finance Sentry alerts (#466)           |
| 7   | 7-nav-a       | [Search lands on the entity](#search-lands-on-the-entity): held names, watchlist and accounts as palette items              | `cmn-command-palette`                                | Finance Sentry shell                   |
| 8   | 8-tx-b        | [Transaction period chips](#transaction-period-chips): monobank-style period chip row beside the restored filters           | `cmn-date-range` presets (`cmn-chip`)                | Finance Sentry transactions            |
| 9   | 9-digest-a    | [Terse digest](#terse-digest): bullets, one headline number each, every bullet linked to its entity                         | none, a content rule                                 | Finance Sentry Ledger digests          |

Pick 8 went against the scout's recommendation (keep the restored bar as is). It is the captain's
explicit word on a filter he uses, so the standing rule (no redesign of a filter or selection
control he uses without his word) is met for this change only. The rest of the restored bar stays.

### Gap table

What the library does not have yet, named so a PR can say which row it closes.

| Missing piece                                               | Pattern source                    | First consumer                                     | When            |
| ----------------------------------------------------------- | --------------------------------- | -------------------------------------------------- | --------------- |
| charts-core crosshair / scrub plus hover and select outputs | Robinhood scrub                   | dashboard hero, dossier chart; pairs with #466 L-1 | same            |
| charts-core multi-series line (benchmark overlay)           | Webull, Taras                     | dossier chart (C2a)                                | November        |
| sparkline mode (`lk-line-chart` minimal or `cmn-sparkline`) | Robinhood positions row           | holdings rows                                      | November        |
| verdict badge plus axis strip                               | Trading Central card, #726 FR-009 | dossier header, ranked universe                    | November (#726) |
| compact stat strip                                          | Taras perf strip                  | dossier                                            | with #824       |
| bull/bear split list                                        | Seeking Alpha                     | dossier thesis                                     | later (C5a)     |
| treemap                                                     | Finviz                            | book heatmap                                       | not now (C4a)   |

Already covered by existing components (no new work): `cmn-chip`, `cmn-command-palette`,
`cmn-alert-item`, `cmn-card`, `cmn-stat-card`, `cmn-tag`, `cmn-disclosure-row`, `cmn-date-range`,
`cmn-month-stepper`, `cmn-donut-chart`, `cmn-area-chart`. The key/value definition grid (Yahoo
Statistics, Taras) is `cmn-description-list`.

## Screen patterns

Each pattern has the same shape: what it is, **Compare against**, **Owner**, **Consumer**, and what
is explicitly not copied. The status of a gap changes in lifekit-common, so check the gap table's
owner before assuming a piece exists.

### Chart period and scrub

The span selector sits directly under the chart, as one row, and holding a finger on the chart
reads a past point.

- **What it is:** a one-row segmented control (1W / MTD / 1M / 3M / YTD / 1Y / ALL) with radiogroup
  semantics and arrow-key roving, plus a crosshair that updates the card's headline number and
  delta while held and snaps back on release. A period is a parameter, not a panel, so it is not
  `cmn-tab-group` (an underline tablist that swaps panels).
- **Fit:** seven segments fit at 390px (326px inside gutter and card padding is about 46px per
  segment, above the 44px target in the lifekit
  [phone contract](https://github.com/lifekit-hq/lifekit-common/blob/main/docs/PHONE-CONTRACT.md)).
- **Colour:** the delta text is coloured, not the whole chart. See [Deltas](#deltas).
- **Compare against:** [Robinhood, Using
  charts](https://robinhood.com/us/en/support/articles/using-charts/) and [Viewing stock
  details](https://robinhood.com/us/en/support/articles/viewing-stock-detail-pages/) (scrub);
  [IBKR Campus, Customizing Charts
  (iPhone)](https://www.interactivebrokers.com/campus/trading-lessons/customizing-charts-iphone/)
  (menu along the bottom of the chart).
- **Owner:** `cmn-segmented`, charts-core crosshair / scrub plugin and hover/select outputs (gap).
- **Consumer:** Finance Sentry dashboard range, events 7/30/90 chips, dossier chart. Pairs with
  Finance Sentry #466 L-1 (chart click outputs), which touches the same plugin layer.
- **Not copied:** Robinhood's whole-screen red/green repaint (see [Deltas](#deltas)).

### Hero card

One card holds the figure: number and delta, then the chart, then the period control under it.

- **What it is:** the Robinhood / IBKR portfolio hero with Revolut's one number per screen. The
  period control still drives the tiles and category widgets below, so it stays a page-level
  control. Scrub updates the number.
- **Compare against:** [Robinhood, Using
  charts](https://robinhood.com/us/en/support/articles/using-charts/); [Revolut, spending and
  income
  analytics](https://help.revolut.com/en-US/help/accounts/budget-and-analytics/how-can-i-see-my-spending-and-income-analytics/).
- **Owner:** `cmn-card`, `cmn-stat-card`, `cmn-area-chart`, plus the chart pieces from
  [Chart period and scrub](#chart-period-and-scrub) (gap).
- **Consumer:** Finance Sentry dashboard. Lands after the chart pieces.
- **Not copied:** a separate chart block below the hero.

### Positions row

A holdings row carries logo, symbol, shares, day change and a sparkline.

- **What it is:** the Robinhood positions row. Day change per row comes first (the quote already
  has `ChangePct`); the 1M sparkline follows when a daily-bars read exists.
- **Compare against:** [Robinhood, Viewing stock
  details](https://robinhood.com/us/en/support/articles/viewing-stock-detail-pages/).
- **Owner:** `cmn-tag` and row primitives today; sparkline mode (gap).
- **Consumer:** Finance Sentry holdings.
- **Not copied:** gamified nudges.

### Entity page order

A holding's page runs: header with price, day % and verdict; perf strip; chart; your position; why
it moved; thesis; key stats; analysts and earnings; events; news.

- **What it is:** the Robinhood / Taras order, with Yahoo's key-stats grid as the completeness
  checklist, StockAnalysis's fair value with its assumptions visible (never a bare number), and
  Zacks's earnings-surprise history once a provider supplies it. Where the page explains a figure,
  it does so in a plain-English line (Simply Wall St).
- **Compare against:** [Robinhood, Viewing stock
  details](https://robinhood.com/us/en/support/articles/viewing-stock-detail-pages/) and [Cortex
  Digests](https://robinhood.com/us/en/support/articles/cortex-digests/); [StockAnalysis, DCF
  calculator tutorial](https://stockanalysis.com/tools/dcf-calculator/tutorial/); [Zacks, Price,
  Consensus and EPS Surprise
  chart](https://www.zacks.com/stock/chart/E/price-consensus-eps-surprise-chart); [Simply Wall St,
  how the Snowflake works](https://support.simplywall.st/hc/en-us/articles/360001740916) (the
  plain-English line only); Yahoo Finance ([2023
  redesign](https://www.businesswire.com/news/home/20231107696257/en/Yahoo-Finance-Debuts-New-Design-and-Features-to-Empower-Everyday-Investors));
  Taras Guk (registry #1).
- **Owner:** `cmn-card`, `cmn-stat-card`, `cmn-tag`, `cmn-disclosure-row`, `cmn-description-list`
  (key stats); compact stat strip (gap), bull/bear split list (gap, later).
- **Consumer:** Finance Sentry asset dossier (#824).
- **Not copied:** the Snowflake radar, Yahoo's ad density, coverage-maximalism.

### Verdict idea card

One instrument, one call, what would change it, a horizon.

- **What it is:** Trading Central's idea card, combined with per-axis transparency:
  1. **Call:** the verdict word (STRONG BUY / BUY / WAIT / AVOID; NOT SCORED in neutral grey).
  2. **Facts:** the four raw axis facts beside it (structure, fundamentals, crowding, valuation),
     each expandable to its evidence.
  3. **What would change it:** generated from the ordered rule list, the unmet conditions of the
     next rule up and the nearest AVOID trigger.
  4. **Horizon:** the thesis horizon when a thesis exists, otherwise omitted, never invented.
  5. **Why this word:** the fired rule, its facts and the rules version.
- **Compare against:** [Trading Central ETF Trade
  Ideas](https://newsletters.tradingcentral.com/direxion/etf_en.html) and [trading
  horizons](https://investor.tradingcentral.com/technical-insight); [Seeking Alpha, Quant Ratings
  and Factor Grades
  FAQ](https://help.seekingalpha.com/premium/quant-ratings-and-factor-grades-faq) (axes kept
  separate, and the warning about a weighted composite).
- **Owner:** verdict badge variant set with a NOT SCORED neutral (gap), axis strip of chips plus
  `cmn-disclosure-row` (gap). The card composes from `cmn-card`.
- **Consumer:** Finance Sentry dossier header and ranked universe (#726).
- **Not copied:** pattern signals as an input, sponsored placement, any composite or weighted
  score, A-F letter grades and radar visuals (out of scope in the #726 spec).

### Scoped notifications

An alert is about something you hold, says why it matters, and opens its entity.

- **What it is:** IBKR's scoping and content. Earnings alerts carry consensus EPS and
  before-open/after-close timing; one deduplicated rating-change alert per held name per day links
  to the dossier's analyst card. Held names are on by default, the watchlist is opt-in (Robinhood
  does the same). Push text stays amount-free.
- **Compare against:** [IBKR, For You
  notifications](https://www.interactivebrokers.com/en/trading/for-you-notifications.php) and
  [FYI notifications](https://www.ibkrguides.com/traderworkstation/fyi-notifications.htm);
  [Robinhood, Price alerts](https://robinhood.com/us/en/support/articles/price-alerts/) (watchlist
  off by default) and [Smart
  Notifications](https://robinhood.com/us/en/newsroom/smart-notifications/).
- **Owner:** `cmn-alert-item`, `cmn-chip`. Nothing missing.
- **Consumer:** Finance Sentry alerts, on top of #466 (every notification links to its entity).
- **Not copied:** mail-blast cadence, legalese, percent-move price alerts (IBKR and Binance
  already alert on price).

### Search lands on the entity

Typing a ticker, an account or a budget opens its page.

- **What it is:** held names, watchlist names and accounts passed to the command palette as items
  at open time, each landing on its entity page. The palette filters client-side by label and
  group. No new screen; names you do not hold are out (curated universe).
- **Compare against:** [Robinhood, A New Way to Navigate
  Robinhood](https://robinhood.com/us/en/newsroom/a-new-way-to-navigate-robinhood/); Yahoo Finance
  ([2023
  redesign](https://www.businesswire.com/news/home/20231107696257/en/Yahoo-Finance-Debuts-New-Design-and-Features-to-Empower-Everyday-Investors)).
- **Owner:** `cmn-command-palette`. A per-item subtitle or avatar slot (ticker plus broker) is an
  optional later addition.
- **Consumer:** Finance Sentry shell (the palette items it passes). No lifekit-common change needed
  now.
- **Not copied:** async server search for names you do not hold.

### Transaction period chips

A fast period chip row (This month / Last month / Last 90 days) beside the transaction filters.

- **What it is:** the monobank list filters (period, category, search), met by the restored filter
  bar, with a period chip row added by the captain's pick (8-tx-b). The row is `cmn-date-range`
  with `[presets]`: a `cmn-chip` per preset above the two date fields. Choosing a chip sets both
  bounds and emits the usual change; the chip whose range matches the value reads as selected
  (`aria-pressed`), so a hand-edited range clears it. The built-ins (`DATE_RANGE_PRESETS`: this
  month, last month, last 30 days, last 90 days, year to date) resolve against `DATE_RANGE_TODAY`;
  a consumer can pass a subset or its own `{label, range}` pairs. It is a chip row, not
  `cmn-segmented`, because the list is the consumer's and can outgrow five short segments, and the
  fields stay beside it for a custom range.
- **Compare against:** monobank statement and transaction filters ([monobank knowledge
  base](https://monobank.ua/en/knowledge-base/acquiring/online/website/platforms/statement)).
- **Owner:** `cmn-date-range` presets (built on `cmn-chip`). Nothing missing.
- **Consumer:** Finance Sentry transactions.
- **Not copied:** statement export (monobank already exports statements), bank-only surfaces,
  gamification.

### Terse digest

A digest is short bullets, one headline number each, every bullet linked to its entity.

- **What it is:** StockAnalysis "Market Bullets" and the Capitellas / Dapper Dividends format:
  short sections, link-outs, no prose essay. A content rule, not a component.
- **Compare against:** StockAnalysis.com (registry #10); Robinhood [Cortex
  Digests](https://robinhood.com/us/en/support/articles/cortex-digests/) for the "why it moved"
  shape.
- **Owner:** none (a writing rule for the consumer's brief generators).
- **Consumer:** Finance Sentry Ledger digests (PerformanceBrief, FireBrief).
- **Not copied:** coverage-maximalism.

### Period reports

A period report is a month stepper, a few headline tiles and a table, not a new screen.

- **What it is:** IBKR statements as period reports and the period picker, with Revolut's
  predicted month-end spend on budgets.
- **Compare against:** [IBKR, FYI
  notifications](https://www.ibkrguides.com/traderworkstation/fyi-notifications.htm); [Revolut,
  spending and income
  analytics](https://help.revolut.com/en-US/help/accounts/budget-and-analytics/how-can-i-see-my-spending-and-income-analytics/).
- **Owner:** `cmn-month-stepper`, `cmn-date-range`, `cmn-stat-card`. Nothing missing.
- **Consumer:** Finance Sentry flow breakdown and budgets (no change planned).

### Book heatmap

- **What it is:** Finviz's treemap of the book. Reference only until November.
- **Compare against:** [Finviz heatmap
  explainer](https://fffinstill.com/learning/concepts/s-p-500-heatmap).
- **Owner:** treemap (gap, not now).
- **Consumer:** none yet.
- **Not copied:** coverage-maximalism.

## Finance Sentry as built

Checked against `main` on 2026-11-06. These are the places where the app's code is not what a rule
above says, so a reader does not assume the rule is already met. Each is a follow-up, not part of
this change.

- **Formatter.** The rules name the library's `formatMoney` / `cmnMoney`. The app formats through
  its own `MoneyUtils.format` and `money` pipe over a fixed `en-US` locale, not `LOCALE_ID`. It is
  symbol first for USD, EUR, GBP and UAH only (`$18,981.48`, `-€1,200.00`) and prefixes the ISO
  code for every other currency (`CHF 950.00`), where the rule shows an ISO code only when two
  currencies sharing a symbol are on one screen. The app's frontend ESLint config is the library
  preset (`createEslintConfig`); the `MoneyUtils` / `money` pipe rule in
  [frontend-rules.md](../claude/frontend-rules.md) is the gate this repo relies on.
- **Empty value.** The formatter renders a missing amount as `—` (an em dash), where rule T2 names
  "-" for an empty table cell.
- **Signed amounts.** `MoneyUtils.format` takes `signed: true` for a leading `+` and never prints
  `-$0.00` for a value that rounds to zero.
- **Base-currency equivalent.** `MoneyUtils.formatEquivalent` renders the muted second line
  (`~ $1,235`, whole units).
- **Fixed colours.** The app already reads `gain`, `loss`, `flow-in`, `flow-out` and the
  `asset-*` tokens in templates and styles; `delta-up` / `delta-down` are the deprecated aliases
  and are not to be used in new code.
