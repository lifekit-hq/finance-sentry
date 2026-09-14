# Feature Specification: Broad-universe opportunity scanning

**Feature Branch**: `20260910-001634-broad-universe-scan`

**Created**: 2026-09-10

**Issue**: #558

## Problem

The ledger-scan opportunity scanner (source `Scan`) only nominates tickers that already have
persisted daily bars, i.e. `held STK ∪ watchlist ∪ 13 seed ETFs`. It re-nominates the book instead
of hunting the market.

`RadarUniverseService.SyncAsync` composes the Radar universe from seed ETFs + every active user's
STK holdings + watchlists. `IngestDailyBarsCommand` is the only `DailyBar` writer and it iterates
exactly that universe, so the bar table never contains a name Denys does not already own or watch.
`OpportunityScanJob` nominates from `MarketStructureReader.GetUniverseStructuresAsync`, which is
computed from those bars — so the momentum side of the scanner is boxed into the book by
construction. The EDGAR fundamentals scorer is *not* bar-limited (it scored PLTR 91/100 with no
bars), so market-wide quality ranking already exists; only breadth of price history is missing.

## User stories

- **US1 — Broad-universe bar ingestion.** As the scanner, I want daily OHLCV bars for the S&P 500
  constituents persisted on the nightly cadence, so momentum/relative-strength can be computed for
  names outside the book. Config-gated (`Radar:BroadUniverseEnabled`) and rate-bounded so a
  500-ticker fetch cannot swamp the nightly run or the upstream provider.
- **US2 — Quality × momentum nomination across the ingested universe.** As Denys, I want the scan
  nomination path to rank every ingested ticker by combined fundamentals quality × momentum, so a
  Scan candidate can be a name I neither hold nor watch.

- **US5 — A two-stage funnel, not a wider brute force.** As Denys, I want the scan to reach the whole
  market through a cheap mechanical pre-filter that hands a bounded shortlist to the expensive
  per-ticker computation, so "high quality + good momentum" is found *in a good way*: no code path
  runs market-structure computation over the full constituent universe, and nothing ingests the whole
  index nightly. (Supersedes US1's rotating whole-index ingestion and US2's unconditional
  full-universe ranking — issue #558, consolidated 2026-09-13.)

- **US3 — Enabled, and over a universe worth trusting.** As Denys, I want the flag actually on in a
  runnable configuration and the seed list verified against the source that ingests it, so the
  broadened universe is real in production rather than only in tests, and a seeded ticker is a name
  that can actually gain a bar.

## Functional requirements

- **FR-001** A ticker list of index constituents is available to the Radar module without Radar
  depending on the Research module (the constituent JSON is an embedded Research resource today).
- **FR-002** When `Radar:BroadUniverseEnabled` is true, index constituents join the Radar universe
  as a distinct membership kind; ownership kinds (holding/watchlist/seed ETF) still win for a
  ticker that is both.
- ~~**FR-003** Bar ingestion never issues more than `Radar:BroadUniverseMaxIngestPerRun` upstream
  fetches for index-constituent members in a single run.~~ Superseded by FR-009/FR-011: the universe
  is the shortlist, so there is no whole-index fetch left to budget.
- ~~**FR-004** Broad-universe ingestion rotates the least-fresh constituents.~~ Superseded by
  FR-011 — nothing outside the shortlist is ingested, so there is nothing to rotate.
- **FR-005** With the flag off, behaviour is identical to today (no extra members, no extra fetches).
- **FR-006** (US2) The scan nomination path ranks candidates across the whole ingested universe by
  a combined quality × momentum score rather than iterating the tracked set only.

- **FR-007** (US3) `Radar:BroadUniverseEnabled` is set true in a configuration a deployed host
  actually reads, and both the enable and the disable path are documented.
- **FR-008** (US3) Every symbol in the constituent seed resolves at the ingestion source, and the
  seed records where it came from and how to regenerate it.

- **FR-009** (US5) A deterministic stage-1 pre-filter composes a bounded shortlist (cap
  configurable, tens by default) from signals that need no per-ticker bar math: the EDGAR
  fundamentals screen over the index constituent list, a batch quote / percent-change read for coarse
  momentum, and the analyst-actions feed. Its composition rules and thresholds live in code and are
  unit-tested, and the whole stage is config-gated for rollout and rollback.
- **FR-010** (US5) Stage 1 bounds its own upstream cost too: the index is ranked on the batched quote
  and street signals first, and only a configured grading budget of names is sent to EDGAR.
- **FR-011** (US5) Bars are ingested and the full structure / relative-strength computation runs only
  for shortlist ∪ held ∪ watchlist (plus the seed lenses the reader needs). A scan cycle over N
  constituents with shortlist cap K computes structure for at most K + |held| + |watchlist| tickers,
  and a constituent outside the shortlist is not ingested even when it still carries stored bars.
- **FR-012** (US5) Every stage-1 upstream may fail without failing the cycle: an empty shortlist
  degrades the universe to its core members, which is exactly the flag-off behaviour.

## Success criteria

1. ~~A scheduled job persists daily bars for S&P 500 constituents on a regular cadence.~~ *(US1 —
   superseded by 5: the nightly job persists bars for the shortlist and the book, never the index.)*
2. ~~The scanner's momentum-side ranking iterates the full ingested bar universe.~~ *(US2 —
   superseded by 5.)*
3. After ingestion, a ledger-scan cycle yields at least one `Scan` candidate that is neither held
   nor watchlisted and carries both a fundamentals score and a momentum rank. *(US2, US3 — proven
   through the real reader → rules chain by `BroadUniverseScanSeamTests`)*
4. The broad universe is on in the API host's configuration, and every seeded symbol is one the
   ingestion source can resolve. *(US3)*
5. A scan cycle costs the per-ticker structure computation for at most K + |held| + |watchlist|
   tickers, pinned by a test over an index of N ≫ K constituents that all carry bars. *(US5)*

## Out of scope

- Non-US listings; Nasdaq-100 names absent from the S&P 500 seed list.
- Intraday bars — the cadence stays daily post-close.
- Changing the fundamentals (EDGAR) scorer — it is already market-wide.
- A Ledger cognition step in stage 1. Decided 2026-09-13: the shortlist is a determined computation
  over numeric signals, so it stays in code where `dotnet test` is the verdict of record; cognition
  keeps its own `score_candidate(source:"Ledger")` lane.
