# Finance Sentry framework primer

Read this before reasoning about any number from this server. It is the short map; the long sources it
points to win on any detail: `docs/finance-framework.md` (the rules), `docs/money-semantics.md` (what each
figure means today), `specs/ROADMAP.md` (destination, Radar) and constitution §VII (the reader boundary).

## What Finance Sentry is, and why

One person's money across banks (Monobank, TrueLayer banks), brokerage (IBKR, Inzhur), crypto venues
(Binance, Revolut X) and the people they share costs with, aggregated into one book. The destination
(ROADMAP, Goal): earn a decent amount of money with the watching done by an agent that briefs concisely on
what matters and stays silent otherwise, finds opportunities early, protects held positions, measurably
beats the benchmark, and judges every recommendation against the person's own strategy, the IPS
(`get_ips`).

## Server computes, the reader interprets

- Every figure is computed here, once, by a pure measure engine; adapters never compute a return, and
  presentation only formats (finance-framework §2.1). A reader never re-derives a total, a return or a
  conversion from raw rows: it quotes the server's figure with its quality and explains it.
- Radar has tiers (ROADMAP, Core architectural idea). Tier 1: deterministic scanners (thesis, market
  structure, opportunity, portfolio, events) append to one signal log (`list_signals`,
  `get_radar_summary`); most signals stay silent, and only a signal past a materiality bar raises an Alert
  (`list_active_alerts`). Tier 2: the reasoning layer reads signals, alerts, fundamentals and the book over
  MCP, interprets them against the IPS and delivers. It never computes signals itself.
- The boundary (constitution §VII): MCP is the reader's only tool surface; the event hook carries ids
  only, so resolve every id by reading back through MCP; no surface is named after a consumer.

## The ten measures (finance-framework §3.4)

- **M1 Value change**: end − start, flows included; a percent only when the scope did not change.
- **M2 Contributions vs growth**: growth = value change − net external flows.
- **M3 Time-weighted return**: the number to compare with a benchmark; flows removed.
- **M4 Money-weighted return**: what the person's money earned, timing included (XIRR, Modified Dietz).
- **M5 Net-worth bridge**: why net worth changed, line by line (finance-framework §3.3).
- **M6 Gain vs cost**: up or down on what is still held, by average cost.
- **M7 Allocation**: what the money is in; liabilities shown separately.
- **M8 Risk**: volatility and drawdown.
- **M9 Benchmark-relative**: book TWR minus the benchmark over the identical window.
- **M10 Projection**: a savings path plus one shared, disclosed return assumption, shown as a range.

Several are not wired yet (finance-framework §6 Known gaps). `get_book_performance`'s `bookTwr` is M3 on the
cash-inclusive brokerage and `delta` is M9; its `book` headline is M1 on invested value, a change that includes
deposits, and `holdingsBasedReturn` is the deposit-free price return. Never call a value change a return.

## The headline contract (finance-framework §3.6)

Every top-line figure carries one record: `amount`, `percent`, `method` (VALUE, VALUE_CHANGE,
CONTRIBUTIONS_VS_GROWTH, TWR, MWR_XIRR, MODIFIED_DIETZ, GAIN_VS_COST, HPR), `basis`, `scope`, `window`,
`quality` (COMPLETE, ESTIMATED, DEGRADED, UNAVAILABLE), `basisStatus`, `coverage`, typed `reasons`,
`asOf`, `currency`.

- State the quality whenever it is not COMPLETE, and name the reason codes in plain words.
- UNAVAILABLE is "not known", never 0 or 0%. A null percent means the method or coverage does not allow
  one, not that it is zero.
- One label, one numerator: "change" is M1, "growth" is M2, "return" names its method. No annualising under
  one year. Never hide a sign.

## Flows and signs

- Five flow classes (finance-framework §3.2): `external_inflow`, `external_outflow`, `income`,
  `fee`/`tax`, `internal`. Income, fees and taxes are return, never flows. Classification depends on scope:
  a transfer is internal for the whole umbrella and external for one source. A newly linked account is not
  growth. Unknown flows degrade a figure (`UNKNOWN_FLOW`); they are never guessed.
- Liabilities (money-semantics §2 Liability sign convention): a credit account's balance is an amount
  owed, stored positive per account, shown with a minus sign on Accounts and subtracted in every net total.
  Never add it as an asset.

## Valuation and currency

- Five steps per position (finance-framework §5.3): principal market, exit price, reliability (L1 quote,
  L2 observable, L3 estimate), convertibility (CASH, IN_ECONOMY, LOCKED) and base currency at the
  valuation date's rate. A unit with no route to the base currency is UNAVAILABLE, never converted 1:1.
- Total net worth is the headline; "of which liquid" (CASH minus liabilities) sits beneath it (C5).
- Compute in USD, show in the base currency (C4; money-semantics §3 Currency conversion). MCP figures
  are USD unless a field says otherwise. Native amounts in different currencies are never summed.

## Decided and open calls (finance-framework §1)

Decided: C3, C4, C5, C7(i), C8, C9.
Open: C6, C1, C2, C7(ii).

C3: the book headline is cash-inclusive brokerage, benchmark SPY. C8: one disclosed projection assumption
set, shown as a range. C9: per-holding checks, each threshold cited, no portfolio-level score. An open call
is the captain's: present the trade-off, never settle it.

## Where each number comes from

| Question | Tools | Meaning |
|---|---|---|
| What do I have? | `get_portfolio_snapshot`, `get_account_summary`, `get_net_worth_history` | money-semantics §1, §8 |
| Where did money go? | `get_cashflow_report`, `list_transactions`, `get_budget_status` | money-semantics §5, §6 |
| How is the book doing? | `get_book_performance`, `get_benchmark_track_record`, `get_track_record` | money-semantics §10 |
| What is it in, and is it within policy? | `get_allocation_vs_target`, `get_ips`, `check_risk_rules` | finance-framework §3.4 (M7, M8) |
| Can I trust the feeds? | `get_sync_health` | finance-framework §5.7 |
| What is the market doing? | `get_market_regime`, `get_sector_rotation`, `get_market_structure` | specs/ROADMAP.md |
| What changed for a holding or thesis? | `list_theses`, `list_thesis_breaks`, `get_fundamentals`, `get_recent_filings` | specs/ROADMAP.md |
| What is coming up? | `get_event_calendar`, `get_earnings_calendar`, `get_macro_calendar` | specs/ROADMAP.md |

For a number not covered here, read the tool's description, then the money-semantics section it cites.
