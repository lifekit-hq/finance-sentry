# Radar — Program Roadmap

**Created**: 2026-07-07
**Status**: Draft for review
**Owner**: Denys
**Origin**: Ledger conversation 2026-07-07 (memory-sector rotation miss → "opportunity radar + thesis radar" proposal), reconciled with the existing `017-thesis-monitor` design pass, then widened per Denys's direction: *"not just a radar for theses — a huge radar that checks status, checks market, checks a lot of stuff."*

## Backlog — specs not yet implemented

> **Single source of truth for *done-or-not* is each spec's `Status:` line** (flipped to `Implemented` at merge). This table only orders what's left and why — it holds priority/sequencing, not status. If a spec here has flipped to `Implemented`, drop it from this list.

| Spec | Kind | Depends on | Note |
|---|---|---|---|
| `032-agent-as-code` | Architecture | 031 (proves the pattern) | Draft — agent definition in the repo, CI-deployed to the runtime |
| `026-event-bus-outbox` | Platform | — | In-monolith broker + transactional outbox (031 is a focused precursor) |
| `027-k8s-migration` | Platform | — | Single-node cluster replaces compose in prod. **Carries 025 fast-fail fix** (short connect-timeout / split long-poll cluster — see 025 spec Known Limitation) |
| `028-extract-market-data-service` | Platform | 026, radar stable | First module extracted to its own service |
| `029-grpc-internal-contract` | Platform | 028 | One internal call goes contract-first RPC |

*(`002-investment-tracking` is `Superseded` — delivered under 008/009/010 + the research suite — not backlog.)*

## Goal

**Earn a decent amount of money, with Ledger taking care of the watching.** Concretely: a financial agent that briefs Denys concisely on what matters (and stays silent otherwise), tracks and analyzes the market on professional-grade inputs, reasons over accumulated data, and helps accumulate wealth — finding opportunities early, amplifying Denys's own convictions with evidence, protecting held positions, and **measurably** beating the benchmark — while every recommendation is judged against Denys's own strategy (the IPS). The 2026-07-07 DRAM/rotation episode is the motivating failure: Ledger explained a single-name catalyst while missing the sector-wide rotation, because Finance Sentry has no market-structure data and no accumulated signal history.

## Core architectural idea: one Radar, many scanners

**Radar is a platform, not a feature.** It has three parts:

1. **Scanners (tier 1, deterministic)** — independent, pluggable jobs inside Finance Sentry. Each watches one domain, computes signals from data, and appends them to a shared signal log. No LLM in any scanner. Each scanner is its own bounded feature and ships independently:
   - **Thesis scanner** (`017`) — invalidation triggers vs EDGAR fundamentals. *Spec ready.*
   - **Market-structure scanner** (`018`) — relative strength, sector rotation, breadth, unusual moves. *The one that would have caught the 2026-07-07 rotation.*
   - **Opportunity scanner** (`019`) — scores candidates (user-seeded convictions + rotation leaders) against structure + fundamentals.
   - **Portfolio scanner** (exists in pieces) — concentration, allocation drift vs IPS (`GetAllocationVsTarget`), sync health. Formalized as signals later.
   - **Event scanners** (exist in pieces) — earnings calendar, filings, macro calendar, news. Already MCP-queryable; emit signals later.

2. **The signal log (the accumulation layer)** — one append-only `radar_signals` store shared by all scanners (introduced in `018`). Most signals are silent — recorded, not alerted. This is what "a lot of data accumulated" means concretely: three weeks of memory-sector RS deterioration is *in the log* before the gap-down, so conclusions can cite trend, not just today. Alerts (existing `012` module) stay the loud tier: a scanner raises an Alert only when a signal crosses a materiality bar.

3. **The reasoning layer (tier 2 — Ledger)** — reads accumulated signals, alerts, fundamentals, and the book via MCP; interprets, narrates, and delivers to Telegram. **Decision framing is strategy-driven: every recommendation must be judged against the IPS** (already stored in Finance Sentry — `SaveIps`/`GetIps`), i.e. "does this fit Denys's declared risk, concentration, and horizon rules," not generic advice. Ledger never computes signals itself.

One-way dependency discipline (constitution Principle VII, ruled 2026-09-17): MCP is Ledger's only tool surface; the hook payload that wakes Ledger carries ids only; no endpoint may name Ledger. Finance Sentry raises domain Alerts and events and exposes MCP reads; Ledger reads back.

> **Is this buildable without paid feeds?** Yes. Commercial versions of this (Bloomberg alerts, Koyfin, TrendSpider) are expensive because they serve every instrument at tick granularity. This Radar needs ~50–100 tickers at daily granularity: Yahoo (already integrated) + EDGAR (already integrated) + RSS news (already integrated) cover it. The moat is not the data — it's the accumulated signal history + thesis discipline + an agent that knows the book and the strategy.

## The pipeline

```
                    ┌────────────────────── RADAR (Finance Sentry, tier 1) ──────────────────────┐
                    │                                                                             │
 Yahoo bars ──►  018 market-structure scanner ──┐                                                 │
 EDGAR      ──►  017 thesis scanner ────────────┼──►  radar_signals log  ──►  Alerts (existing)   │
 holdings   ──►  019 opportunity scanner ───────┘          │                        │             │
                    │                                      ▼                        ▼             │
                    └────────────────────────────── MCP tools ────────────────────────────────────┘
                                                           │
                                                           ▼
                                        Ledger (tier 2): interpret vs IPS, deliver
                                                           │
                                                           ▼
                                     InvestmentThesis (the book) ──► 020 track record
```

| # | Feature | Role | Risk | Status |
|---|---|---|---|---|
| 017 | Thesis Break Monitor | Defense: deterministic invalidation of held theses (fundamentals **+ price** triggers) | Low | **Spec ready** — implement first |
| 020 | Thesis Track Record | Honesty: price-stamp every thesis/candidate; net-of-cost excess return vs SPY | Low | Spec drafted (`020-thesis-track-record`) — **v0 ships second** to start the measurement clock |
| 018 | Market Structure Scanner + Signal Log | Eyes + accumulation layer all other scanners write to | Low-medium | Spec drafted (`018-market-structure`) — includes log-only calibration phase + historical validation |
| 022 | Risk Rules | The practitioner layer: position sizing, max loss per thesis, concentration remediation | Low | **Implemented** (`022-risk-rules`) — **gates 019** |
| 019 | Opportunity Scanner | Offense: conviction scorecards + promote → thesis. Machine scan deferred to v2 | Medium | **Implemented** (`019-opportunity-scanner`) — US1 scoring + US3 promote/reject/expire; scan (US2) deferred to v2 |
| 021 | Market Regime Scanner | Context: regime — VIX, yield curve (sentiment indices cut per review) | Low | Sketch below — spec after 018 |

## Independent review (2026-07-07)

Two fresh-context judges reviewed the full roadmap + specs: a trading practitioner and a software architect. Both returned **sound-with-changes**; their accepted findings are folded into the specs and this sequencing. The load-bearing ones:

- **Defense before offense**: risk rules (022) and price-based 017 triggers matter more than any scanner — EDGAR fundamentals lag price by months, so a fundamentals-only monitor cannot protect a position intraquarter.
- **Measurement starts immediately**: 020-v0 moves to second — a track record only accrues value with elapsed time.
- **No untuned alerts**: 018 runs log-only for 2–4 weeks; alert thresholds are set from observed signal distributions and validated against ≥5 years of historical bars (2020 crash, 2022 growth unwind, 2026-07 rotation as fixtures — generalizing beyond the single DRAM episode).
- **Deferred as evidence-weak or premature**: `trim_into_strength` composite (fights momentum evidence; v2 after historical validation), 019's scheduled scan (chase-machine risk on untuned thresholds; v2), 019's composite single-number score (false precision — scorecard facts only), sentiment indices in 021 (VIX/yield curve stay), 5-day rotation deltas (windows lengthened toward evidence-backed 1–3+ months).
- **Operability**: 018 gets a data-freshness watchdog (stale bars/failed runs raise an Alert); `radar_signals` gets `PayloadVersion` + a retention policy; price history goes behind a thin source interface so Yahoo isn't a single point of failure.
- **Honest effort estimate**: ~4–6 solo weeks for 017+020+018+019 v1.

### 021 sketch — Market Regime Scanner ("the professional-grade context")

Trimmed per the 2026-07-07 review to the two regime inputs with real evidence behind them (sentiment indices — CNN Fear & Greed, crypto Fear & Greed — cut as folklore-grade):

- **VIX** (`^VIX` via the existing Yahoo client) — level + trend → risk-on/risk-off regime signal.
- **Yield curve / rates** (FRED, free API key) — 10y–2y spread, regime for growth vs value.

Same pattern as every scanner: daily Hangfire job → deterministic regime classification (config thresholds) → `radar_signals` (`regime_change` notable, daily `info` readings) → MCP `get_market_regime()`. Regime is *context* for 019 scoring and Ledger briefs, never auto-actions. Depends only on 018's signal log; spec it once 018 is in review.

**IMPLEMENTED (`021-market-regime`).** Built inside the Radar module (schema `radar`, migration M002 `regime_readings`). Two orthogonal axes, never merged: **volatility** from `^VIX` (Yahoo client) → Calm/Normal/Stressed/Panic + trend; **rates** from FRED `DGS10`/`DGS2` 10y-2y spread → Steep/Normal/Flat/Inverted + recession flag + growth-vs-value tilt. New keyless-silent `FredYieldCurveSource` (`FRED_API_KEY` → `Regime__Fred__ApiKey`; blank = rates axis silent, VIX still works). Daily `regime-compute` Hangfire job writes daily `info` per axis + a `regime_change` notable on band cross (silence-window deduped). `get_market_regime()` MCP tool returns both axes. 019 coupling via Core port `IMarketRegimeSource`: a deterministic, config-driven haircut on the *regime-adjusted* structure score (Stressed/Panic scaled by crowding, +inversion haircut) — raw persisted score untouched, context never actions (stay-invested default holds). Sentiment indices stay cut.

### 022 — Risk Rules (see `022-risk-rules/spec.md`)

The practitioner layer the review found missing: written, machine-checked position policy — max position weight, max loss per thesis, remediation plan for existing concentration (DRAM at ~46%), add-to-broken-thesis flag. Deterministic checks against the live book; violations are signals/alerts; 019's promote flow refuses silently oversized bets. Worth more than any scanner for a concentrated book.

## Sequencing & dependencies (revised per 2026-07-07 review)

1. **017 first** — spec is ready, protects the DRAM position (~46% of book) today, including price-based triggers that don't wait for filings. No dependency on the rest. (It predates the signal log; wiring its run summaries into `radar_signals` is a small follow-up, not a blocker.)
2. **020-v0 second** — events + quote-service prices, needs only 017. Starts the measurement clock immediately; upgraded to bar-based pricing when 018 lands.
3. **018 third** — signal log + market-structure scanner, shipped in two phases: (a) ingestion + computations + log-only signals; (b) alerts enabled after 2–4 weeks of calibration and historical validation.
4. **022 fourth (small)** — risk rules; gates 019's promote flow.
5. **019 fifth** — conviction scoring + promote (v1). The scheduled scan and any composite score wait for calibrated 018 data (v2).

Later scanners (021 regime, portfolio, events/news) plug into the same signal log as small independent features — no new architecture needed.

## Ledger-side work (outside this repo, tracked here for completeness)

- Update Ledger's analyst-loop prompts to lead with the Radar: `get_radar_summary`/`list_signals` + `get_sector_rotation` first, single names second — the 3-layer answer format agreed 2026-07-07 (what moved → where money rotates → what it implies).
- Every buy/add/trim recommendation must cite IPS fit (via `get_ips` + `get_allocation_vs_target`) — the agent pursues Denys's strategy, not generic market takes.
- Adopt two TradingAgents prompt patterns (see Prior art): **bull/bear debate** before any conclusion, and a **risk-veto pass** against the IPS before any recommendation is delivered.
- **Pre-earnings setup brief**: when a held/proxy ticker has earnings within N days (existing earnings calendar) AND the position setup is stretched (018: extension, `trim_into_strength` inputs, book weight; 021: regime), Ledger sends a brief framing the *asymmetry* — what's priced in, how crowded the setup is, what a trim would do — never a direction prediction. This is the "ping me before the Micron report" flow.
- **Promote-time ritual (2026-07-08 gap-check)**: before any thesis is promoted, Ledger runs — bull/bear debate, **premortem** ("it's a year later and this lost 40%; write the history"), **outside-view check** (base rates for the implied growth), and the 022 risk gate. A short do-confirm checklist, not an essay.
- **Pre-exit ritual (2026-07-08 gap-check)**: exits get the same rigor as entries (institutions demonstrably sell worse than random). Any sell discussion must name the *reason class* (thesis broken / policy remediation / better use of capital — "sell into what?") and reject "it's up" / "it's down" as reasons on their own.
- **Stay-invested default**: regime and structure signals inform sizing and entries; Ledger never recommends raising cash on macro concern alone.
- **Investor-circumstance monitoring**: quarterly, Ledger asks Denys about changed cash needs / horizon / income (the CFA feedback loop monitors the investor, not just the market) and reviews the IPS document itself on a schedule.
- Ledger owns delivery (Telegram briefs) and narrative; it never computes scores itself. Briefs stay short: what moved → where money rotates → what it implies — silence when nothing is material.

## Professional-practice gap-check (deep research, 2026-07-08)

A multi-source research sweep (CFA Institute curriculum, Mauboussin/base-rate literature, Marks memos, Barber–Odean retail-investor studies, Morningstar Mind-the-Gap, institutional memo-process surveys, JoF 2023 selling-skill study) was run against this roadmap. Verdict: the scanner/thesis/risk/track-record core matches the professional canon, and two of our choices (counterfactual tracking of rejected ideas; red-team/judge review) are *above* common institutional practice. Five gaps were found and adopted:

1. **Turnover guardrail — the highest-evidence finding anywhere in the sweep.** Barber–Odean (66,465 households; the *median* household in that study held $16,210 — exactly this book's scale): the most active retail quintile earned 11.4%/yr net vs 18.5% for the least active — a 7pp/yr penalty from trading frequency alone, with gross returns nearly identical. Morningstar measures a persistent ~1.2pp/yr self-inflicted timing gap, worst in volatile/concentrated strategies. → 022 gains a **turnover budget** (trades/quarter cap with override logging); the whole system's default is inaction; Ledger briefs never nudge toward action without a rule firing.
2. **Target allocation + written rebalancing policy.** In the CFA canon these are mandatory process stages, not options: documented target weights, allowable drift bands (e.g. ±5%), rebalancing decisions that explicitly net transaction costs and taxes. → 022 gains allocation targets + drift-band checks; rebalancing suggestions weigh tax/cost via 020's friction model.
3. **Expectations-vs-price + base rates in the scorecard.** A good business ≠ a good stock; a thesis is only valid as a *variant perception* — a view not already priced in (Mauboussin/Rappaport). And growth assumptions must be checked against reference-class base rates (year-to-year earnings-growth persistence across 48k company-years is ~zero, r = −0.05). → 019's scorecard gains a "what's priced in" section (market-implied expectations facts) and a base-rate check on any growth assumption; Ledger prompts gain the outside-view step (in a PE-investor experiment, it caused >80% of over-optimists to revise down).
4. **Sell-side discipline beyond breaks.** JoF 2023: institutions with $573M average books show real skill buying and *underperform random* selling — exits get less attention than entries everywhere. Marks: the two dominant real-world sell reasons ("it's up", "it's down") are both errors; every sell must be framed as opportunity cost ("sell into what?"). → Ledger-side pre-exit checklist with the same rigor as entry; 020 already logs both sides to measure it.
5. **Decision journaling + scheduled post-mortems (process vs outcome).** Outcome bias is the failure mode of a returns-only track record: a good decision can lose and a bad one can win. Institutional practice (97% have memo templates; 78% gate investment on memo approval) pairs the numbers with contemporaneous reasoning and scheduled retrospectives. → 020 gains a decision-journal field on every lifecycle event and a scheduled (semi-annual) post-mortem review packet; premortem ("write the failure history before committing") added to Ledger's promote-time prompts alongside bull/bear.

Additions on the margin: lightweight cross-position **correlation facts** from 018 bars (per-position rules understate risk when holdings are correlated — Keynes's "opposed risks" test); simple **stress numbers** (what does −30% on the top position do to the book); IPS upgrades (required-vs-desired return, liquidity constraint, risk tolerance = *lower* of ability and willingness, scheduled review of the IPS itself); "stay invested" default — regime signals inform but never trigger cash-raising (Marks; missing the 10 best days 1999–2018 cut S&P returns from 5.6% to 2.0%/yr).

Explicitly rejected as non-transferable to a $15k unlevered solo book: VaR/CVaR machinery, factor risk-budgeting math, independent-CRO/segregation-of-duties apparatus, ops-risk governance, and tactical cash-raising on macro signals.

## Prior art — TradingAgents (TauricResearch, reviewed 2026-07-07)

Multi-agent LLM trading framework (~92k stars, Apache-2.0, active; arXiv 2412.20138): analyst agents → bull/bear researcher debate → trader → risk manager. **Verdict: borrow patterns, don't use.** Its shape (LLM agents computing everything, execution-oriented) inverts ours (deterministic scanners + one reasoning agent), and ours is right for a no-execution advisory system. Liftable, license-free:

- **Bull/bear debate as a Ledger prompt pattern** — one pass that argues both sides of a thesis before concluding; antidote to sycophantic "signal confirmed" briefs. → Ledger-side work.
- **Risk-manager-as-veto** — final check of every recommendation against the IPS (sizing, concentration, no-trade conditions) before a brief goes out. → Ledger-side work; data via `get_ips`/`get_allocation_vs_target`.
- **Decision log with realized outcomes** — exactly feature 020; validates that design.
- **Data stack confirmation** — their free tier is our stack (Yahoo, FRED) plus two ideas: a pure-local indicator library approach (MACD/RSI computed from persisted bars — fits 018's deterministic core) and **Polymarket odds as a free event-risk input** (candidate for a later event scanner).
- **Avoid**: the full multi-agent graph (5–10+ LLM calls/ticker/day), LLM-generated technicals, and trusting their backtest returns (LLM knowledge-cutoff contamination is unresolved).

## Explicitly deferred (do not let these block anything)

- **Analyst estimate revisions** — no good free source; revisit if a free/cheap feed appears (constitution preference: build over paid APIs).
- **Fund-flow / options-flow data** — same reason. Crowding is proxied in 018/019 (MA extension, volume ratio) instead.
- **LLM-driven trend discovery** (the fuzzy half of Ledger's old `016-thesis-radar` draft) — 019 keeps candidate *scoring* deterministic; free-form "find underhyped trends" stays a Ledger prompt over accumulated signals, not a Finance Sentry feature.
- **Portfolio & event scanners as signal emitters** — the underlying data/tools exist; formalizing them as `radar_signals` writers is post-019 plumbing.
- **Backtesting / historical simulation** — 020 measures forward from creation; no retro-simulation in v1.
- **Trade execution** — never in scope. The system informs; Denys decides.

---

# Platform — Production-Practice Track (added 2026-07-09)

Separate from the Radar program: a deliberate production-engineering ladder (observability → retention → gateway → messaging → orchestration → service extraction → RPC). Each step is one specced, versioned release; order is load-bearing (don't orchestrate what you can't observe).

| Spec | Feature | Depends on |
|---|---|---|
| `026-event-bus-outbox` | Broker + transactional outbox inside the monolith | — |
| `027-k8s-migration` | Single-node cluster replaces compose in prod (also lands the deferred 025 fast-fail fix) | — |
| `028-extract-market-data-service` | Radar/market-data becomes its own service | 026, radar (018) stable |
| `029-grpc-internal-contract` | One internal call goes contract-first RPC | 028 |

### 024 follow-ups (implemented; deferred bits)

- **US3 downsampling ships gated off** (`Retention:Downsample:Enabled=false`). Enable only after validating aggregates on a data copy — it irreversibly compacts `radar.daily_bars` and `net_worth_snapshots` to weekly. (~806 old daily bars would collapse on the current dev DB.)
- **Migration-history-table name mismatch** (pre-existing, surfaced during 024): Budgets/Subscriptions/Alerts and CryptoSync/BrokerageSync use a generic/suffixed `__ef_migrations_history*` name in their runtime module registration vs. their design-time factory. Harmless for logical `pg_dump`/`pg_restore` (the whole DB is dumped/restored), but a future per-schema physical-restore or migration-history audit should reconcile them. Not a 024 blocker.
- **Conservative keep-forever windows**: several genuinely-growing but low-rate tables (`news_articles`, `quote_cache`, `analyst_actions`, `thesis_events`) are `Keep` by decision to avoid RAG-provenance breakage; revisit their windows once provenance is decoupled.
