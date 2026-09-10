# Implementation Plan — Broad-universe opportunity scanning

## Architecture decisions

- **Constituent list stays a single embedded resource.** `sp500-constituents.json` already lives in
  `Modules.Research/Infrastructure/Resources` (embedded, loaded by `AnalystUniverseService`). Radar
  must not reference Research, so the list is exposed through a new Core port
  `IIndexConstituentSource` (precedent: `IBrokerageHoldingsReader`, `IWatchlistReader` — Core
  interfaces implemented by the owning module). Research implements it once and both consumers read
  through it; the JSON is never duplicated.
- **Radar takes the port optionally** (`IIndexConstituentSource? = null`, precedent:
  `DashboardQueryService`'s optional `IBrokerageHoldingsReader`) so the Radar module and its tests
  stand alone when Research is not registered.
- **Broad members are a new `UniverseKind.IndexConstituent`.** `Kind` persists as a string
  (`RadarDbContext` line 67, max 20) so a new enum value needs no migration. Ownership kinds win via
  the existing first-write-wins `Add` in `RadarUniverseService`.
- **Rotation, not a bigger nightly fan-out.** 500 sequential Yahoo fetches per night is both slow and
  a rate-limit risk. Ingestion always fetches core members (seed/holding/watchlist), then fills the
  remaining budget (`BroadUniverseMaxIngestPerRun`) with the least-fresh constituents using the
  existing batch `IDailyBarRepository.GetLatestDatesAsync`. Coverage converges over successive runs;
  freshness of the book is never traded away for breadth.
- **Config gate defaults to off** (`Radar:BroadUniverseEnabled = false`) — same launch posture as
  `ScannerMode.LogOnly`.

## Story slices

### US1 — broad-universe bar ingestion (this increment)

Surface:
- `Core/Interfaces/IIndexConstituentSource.cs` (new port).
- `Modules.Research/Infrastructure/MarketData/Sp500ConstituentSource.cs` (new; owns the embedded
  resource read, cached), registered in `ResearchModule`.
- `Modules.Research/Application/Services/AnalystUniverseService.cs` — drops its private
  `LoadSeed()`/`SeedFile` and consumes the port (no behaviour change).
- `Modules.Radar/Domain/Enums.cs` — `UniverseKind.IndexConstituent`.
- `Modules.Radar/Application/Services/RadarOptions.cs` — `BroadUniverseEnabled`,
  `BroadUniverseMaxIngestPerRun`.
- `Modules.Radar/Application/Services/RadarUniverseService.cs` — appends constituents when enabled.
- `Modules.Radar/Application/Commands/IngestDailyBarsCommand.cs` — core-first ordering + rotation
  budget; `IngestRunSummary` unchanged.

Constraints discovered:
- `RadarUniverseRepository.UpsertMembersAsync` reactivates and overwrites `Kind`, so a ticker that
  becomes a holding later is upgraded on the next sync automatically.
- `MarketStructureReader` treats only Benchmark/Sector/Industry as `IsEtfLens`; `IndexConstituent`
  therefore flows into structure computation and the scan universe as an ordinary ticker — that is
  intended, and is what makes US2 possible.
- The nightly `radar-ingestion` Hangfire job already runs `IngestDailyBarsCommand`; no new job
  registration is needed (success criterion 1 is met by the existing cadence).
- `RadarFreshnessWatchdogJob` alarms on any active member whose latest bar is older than
  `FreshnessMaxTradingDays` (2). Rotation makes most constituents older than that *by design*, so the
  watchdog must skip `IndexConstituent` members or it alarms nightly and buries real book outages.

### US2 — quality × momentum nomination (this increment)

Surface:
- `Modules.Research/Domain/Scoring/ScanNominationRules.cs` — `RankByQualityMomentum` + `ScanCandidateRank`.
- `Modules.Research/Infrastructure/Jobs/OpportunityScanJob.cs` — grades a momentum shortlist through
  EDGAR, re-ranks, then caps.
- `Modules.Research/Application/Services/OpportunityOptions.cs` — `ScanQualityShortlistSize`,
  `ScanQualityWeight`, `ScanQualityLeaderScore`.

Decisions:
- **Momentum first, quality second, both bounded.** The RS percentile is free (it is already in the
  018 snapshot the scan reads), the EDGAR grade costs an upstream fetch per ticker. So the whole
  ingested universe is momentum-ranked, the top `ScanQualityShortlistSize` (25) are graded, and the
  combined score re-ranks only those. Grading 460 constituents per nightly run is not affordable and
  a name outside the momentum top-25 was never going to take one of 5 slots.
- **Combined score = `grade × w + rsPercentile × (1 − w)`**, `w = ScanQualityWeight` (0.5), both
  inputs already on a 0-100 scale. Weight is clamped, so a misconfigured value degrades to
  pure-quality/pure-momentum rather than producing nonsense.
- **A missing half is never given a faked score** (house rule: sub-scores are null, not defaulted).
  Ungraded names — crypto, ETFs, non-filers — and the breakout nominee whose RS window never resolved
  score no combined value; they keep their momentum-only standing and sort after every fully-scored
  name, so they stay nominatable without diluting the quality-first intent.
- **The shortlist bounds the EDGAR fan-out, never the nomination count**: it is taken as
  `max(ScanQualityShortlistSize, ScanMaxNominationsPerRun)`, so a shortlist misconfigured below the
  cap cannot silently starve a run of nominations.
- **Nomination reasons stay stable strings.** `QualityMomentumReason` is a constant tagged on names
  at/above `ScanQualityLeaderScore`; a score-bearing reason string would defeat the candidate's
  reason dedup and accumulate a near-duplicate every run.
- The EDGAR service caches fundamentals per ticker with a TTL, so the shortlist grade and the
  subsequent `ScoreCandidateCommand` fetch for the capped survivors share one upstream call.

Constraint still open (T013): `MarketStructureReader.GetUniverseStructuresAsync` re-runs sector
rotation + affinity per member (~14 queries each), so the scan's read cost grows with the broad
universe even though nothing else does.

Constraints found while shipping US1 — all bite only with the flag on, and all belong to the slice
that turns it on:

- `MarketStructureReader.GetUniverseStructuresAsync` calls `GetStructureAsync` per member, and each
  call re-runs sector rotation + sector affinity (~25 `GetSinceAsync` each). At ~460 members that is
  ~12k round trips per scan — the rotation/affinity work has to be hoisted out of the per-ticker
  path before the scanner reads the broad universe.
- `ComputeMarketStructureCommand`'s per-ticker `extended` / `unusual_move` emission has no
  membership filter and the `unusual_move` dedup key carries no date, so ~450 constituents would
  emit signals nightly. Scope emission to core kinds (the freshness watchdog already got this
  treatment in US1) or the signal feed drowns.
- `GetRelativeStrengthTool` defaults to the whole universe; it needs a cap before the universe
  grows, or one MCP call returns ~460 structures.
- `ScanNominationRules` drops stale structure, so the ingestion budget must keep a full rotation
  inside `FreshnessMaxTradingDays` (why the default is 250, not 100).
- Symbols Yahoo cannot resolve (`BRK.B` — Yahoo wants `BRK-B`) return an empty series rather than
  throwing, so they never gain a bar and sort first in the rotation forever, costing a budget slot
  per run. Harmless at budget 250; a ticker-symbol normalisation (or a negative cache) is the fix.
