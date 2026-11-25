# Finance Sentry
A personal finance aggregation platform (ASP.NET Core modular monolith plus Angular SPA): it syncs bank, crypto and brokerage accounts, computes money figures deterministically, and feeds a companion advisor agent over MCP. The glossary covers the money-semantics vocabulary and the research/discovery vocabulary (thesis, candidate, verdict, universe) a contributor meets in `docs/` and `backend/src/`.

## Language

**Module**:
One bounded slice of the modular monolith (`FinanceSentry.Modules.*`: BankSync, CryptoSync, BrokerageSync, Wealth, Research, Risk, Alerts, Companion, Radar, Events, ...), each with its own database schema and migrations run module by module.

**Provider**:
An external data source an account syncs from (TrueLayer, Monobank, Binance, Revolut X, Interactive Brokers, Inzhur), each behind an adapter. Providers listed in `ConnectionHealth:ActiveProviders` report a connection health state.

**Connection health**:
The provider-neutral state of one linked connection: `Healthy`, `Degraded`, `Failing`, `ActionRequired` or `Dormant`, driven by classified provider failures (`ConnectionHealthPolicy`, `ProviderFailure`).

**Sleeve**:
One kind of wealth source that makes up net worth: banking, brokerage or crypto (manual holdings are a fourth `IWealthSource`). Each is a registered `IWealthSource`; the three stored sleeve columns are frozen history, not extended.

**Book**:
The set of holdings, cash included, that portfolio figures are computed over (`BookFiguresService`, "position share of book", the `book` headline of `get_book_performance`).

**Net worth**:
Value of all assets minus all liabilities across every source in the base currency. **Liquid net worth** is the part whose convertibility is CASH; the headline is the total with "of which liquid" beneath.

**Scope**:
The set of accounts a figure covers: the whole umbrella, the cash-inclusive brokerage, invested positions only, or one account. Every measure takes a scope, and the same transfer can be internal at one scope and external at another.

**External flow**:
Money or assets crossing the scope boundary by the owner's action (deposit, withdrawal, transfer in or out). Classes: `external_inflow`, `external_outflow`; income, fee and tax are return, never flows.

**Internal transfer**:
A move inside the scope (own bank to own bank, Binance spot to Earn). It nets to zero and is never a flow or growth.

**Net-worth bridge**:
The decomposition of any net-worth change over a window into lines that add up exactly: savings, investment return, FX translation, revaluation, newly linked, and a never-hidden unexplained residual.

**Quality**:
The trust grade on a figure: `COMPLETE`, `ESTIMATED`, `DEGRADED` or `UNAVAILABLE`, with typed reasons such as `UNKNOWN_FLOW`. Unknown flows degrade a figure; they are never guessed.

**Ledger**:
The companion finance advisor agent whose persona is versioned in `agent/ledger/` (core plus one runtime adapter). It reads finance-sentry over MCP and proposes; finance-sentry computes. The owner decides to buy or sell.
_Avoid_: finance agent, advisor

**Companion**:
The mode and module through which finance-sentry owns the policy (materiality, mode preference, dedup, quiet hours) for pushing events to the agent, while the agent runtime owns delivery. Path: Alert, then Companion, then Ledger.

**Alert**:
A deterministic raised condition (`AlertType`, severity, silence window) from the Alerts module: thesis broken, filing landed, earnings ahead, cash shortfall, performance brief. Detectors raise alerts; no model call is on the tick path.

**Thesis**:
An `InvestmentThesis`: the owner's monitored reasoning for a ticker, with invalidation triggers; a monitor marks it broken (`BrokenAt`, `BrokenReason`) deterministically. Writes to it return to the owner.

**Thesis revision**:
A pending change to a thesis that Ledger proposes and the owner confirms; promotion and thesis writes stay owner-only.

**Candidate**:
An `OpportunityCandidate`: a nominated ticker under tracking, one active per user and ticker, from source `User`, `Scan` or `Ledger`, with status `Active`, `Promoted`, `Rejected` or `Expired`. Promoting it creates a thesis.

**Watchlist**:
The owner's list of tickers (`WatchlistItem`) that detectors and scoring cover even when not held.

**Verdict**:
The one-word reading a scorecard gives a name from a pure ordered decision list (`VerdictRules`): `StrongBuy`, `Buy`, `Wait`, `Avoid`, or `NotScored`. Recommend-only; the app never places a trade.

**Discovery funnel**:
The staged, mostly deterministic pipeline that finds names without the owner naming them: reference universe, scan and screens, coverage, catalysts, events, then the candidate path.

**Reference universe**:
The pool candidates are drawn from, held as refreshed data (S&P 500, Nasdaq-100, sector and industry lists, theme lists) and read through `get_reference_universe`.

**Coverage universe**:
"Our own index": the capped list (`Coverage:MaxMembers`) of companies the pipeline keeps in view by rules (`held`, `watchlist`, `ledger_nomination`, `buy_twice_in_window`; removal `avoid_twice`, `candidate_expired`, `no_event`), with no confirmation from the owner.

**Catalyst**:
A dated or detected event that can bring a name to notice: an 8-K item, an earnings date, a news cluster, a theme-attention reading. A catalyst nominates; it never adds a coverage member by itself.

**Investment Policy Statement (IPS)**:
The owner's versioned policy (`InvestmentPolicyStatement`): allocation targets, rebalancing rule, review cadence, risk tolerance and capacity. It is the one home of target allocation.

**Risk rule set**:
The enforced limits (`RiskRuleSet`, e.g. `MaxPositionWeightPct`) that trip risk alerts. The one home of the single-position cap; target allocation lives in the IPS, not here.

**Action ticket**:
A propose-only, confirmable order list or cash-sweep proposal produced when allocation bands are breached or idle cash exceeds the buffer. Nothing executes without the owner's acknowledgement.

## Relationships
- A **Module** owns its schema; a **Provider** syncs into **Sleeves**, whose figures sum to **Net worth**.
- Every figure is computed for a **Scope** and carries a **Quality**; an **External flow** is relative to that **Scope**, an **Internal transfer** never is.
- A **Net-worth bridge** splits a change in **Net worth** into named lines plus an unexplained residual.
- The **Discovery funnel** draws from the **Reference universe**, keeps the **Coverage universe**, and nominates **Candidates**; promoting a **Candidate** creates a **Thesis**.
- **Ledger** proposes a **Thesis revision**; the owner confirms. **Ledger** writes **Candidates** and the **Watchlist** directly.
- A detector raises an **Alert**, **Companion** applies policy and hands it to **Ledger**.
- The **IPS** states target allocation; the **Risk rule set** enforces limits; breaches yield **Action tickets**.

## Flagged ambiguities
- "Ledger" is the agent, but the frontend also has a `transaction-ledger` page and store (`docs/claude/app-state.md`, "TransactionLedgerStore"); open issue #1507 exists to keep the name Ledger out of endpoints and payloads.
- "Book": `BookFiguresService` sums banking, brokerage and crypto sources (`docs/finance-framework.md:99`), yet decision C3 says the book headline is the cash-inclusive brokerage (`docs/finance-framework.md:29`).
- "Stage" numbering of the discovery funnel is inconsistent: reference universe is "Stage A" (`docs/reference-universe.md:3`), coverage is "stage 1" (`docs/coverage-universe.md:3`), catalysts are "stage 7" (`docs/catalyst-detection.md:3`), and the scan is a separate "two-stage funnel" (`docs/claude/app-state.md`, #558).
- "Owner" vs "captain" in the docs (`docs/finance-framework.md:25` "captain's review", `docs/coverage-universe.md:3` "owner ruling") for the same decision-maker.
