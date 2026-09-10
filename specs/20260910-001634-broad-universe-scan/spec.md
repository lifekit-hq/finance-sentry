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

## Functional requirements

- **FR-001** A ticker list of index constituents is available to the Radar module without Radar
  depending on the Research module (the constituent JSON is an embedded Research resource today).
- **FR-002** When `Radar:BroadUniverseEnabled` is true, index constituents join the Radar universe
  as a distinct membership kind; ownership kinds (holding/watchlist/seed ETF) still win for a
  ticker that is both.
- **FR-003** Bar ingestion never issues more than `Radar:BroadUniverseMaxIngestPerRun` upstream
  fetches for index-constituent members in a single run; holdings/watchlist/seed members are always
  ingested first and are never displaced by the broad universe.
- **FR-004** Broad-universe ingestion rotates: constituents with no bars at all, then the oldest
  stored bar, are fetched first, so repeated runs converge on full coverage.
- **FR-005** With the flag off, behaviour is identical to today (no extra members, no extra fetches).
- **FR-006** (US2) The scan nomination path ranks candidates across the whole ingested universe by
  a combined quality × momentum score rather than iterating the tracked set only.

## Success criteria

1. A scheduled job persists daily bars for S&P 500 constituents on a regular cadence. *(US1)*
2. The scanner's momentum-side ranking iterates the full ingested bar universe. *(US2)*
3. After ingestion, a ledger-scan cycle yields at least one `Scan` candidate that is neither held
   nor watchlisted and carries both a fundamentals score and a momentum rank. *(US2)*

## Out of scope

- Non-US listings; Nasdaq-100 names absent from the S&P 500 seed list.
- Intraday bars — the cadence stays daily post-close.
- Changing the fundamentals (EDGAR) scorer — it is already market-wide.
