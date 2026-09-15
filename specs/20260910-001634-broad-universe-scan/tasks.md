# Tasks — Broad-universe opportunity scanning

## US1 — broad-universe bar ingestion (config-gated)

- [x] T001 [US1] Add `IIndexConstituentSource` port to `FinanceSentry.Core/Interfaces`.
- [x] T002 [US1] Implement `Sp500ConstituentSource` in Research over the embedded
      `sp500-constituents.json`; register it in `ResearchModule`.
- [x] T003 [US1] Refactor `AnalystUniverseService` to consume the port instead of its private
      embedded-resource loader (no behaviour change).
- [x] T004 [US1] Add `UniverseKind.IndexConstituent` and the `BroadUniverseEnabled` /
      `BroadUniverseMaxIngestPerRun` options.
- [x] T005 [US1] Compose constituents into the Radar universe when the flag is on, ownership kinds
      winning.
- [x] T006 [US1] Ingest core members first, then fill the per-run budget with the least-fresh
      constituents (batch `GetLatestDatesAsync`).
- [x] T007 [US1] Keep the freshness watchdog scoped to core members — rotated constituents are stale
      by design and would alarm it nightly.
- [x] T008 [US1] Tests: constituent source parses the resource; universe composition on/off and
      precedence; ingestion budget, core-first ordering and rotation; watchdog scope.
- [x] T009 [US1] Verify: `dotnet build` warning-free + `dotnet test FinanceSentry.sln`.

## US2 — quality × momentum nomination (next increment)

- [x] T010 [US2] Join the EDGAR fundamentals grade with the 018 structure/RS rank into a combined
      scan score (`ScanNominationRules.RankByQualityMomentum`).
- [x] T011 [US2] Rank the whole ingested universe by that score in the nomination path, keeping the
      per-run nomination cap and bounding the EDGAR fan-out with a momentum shortlist.
- [x] T012 [US2] Tests: a non-held, non-watchlisted constituent can be nominated; ranking order,
      shortlist bound and cap are deterministic.
- [x] T013 [US2] Hoist the per-ticker sector rotation/affinity work out of
      `MarketStructureReader.GetUniverseStructuresAsync` — at ~460 members it is ~6k queries per scan.
- [x] T014 [US2] Tag `QualityMomentumReason` only when a combined score exists — a graded name whose
      RS never resolved has no momentum to claim.

## US3 — turn it on, and make the universe trustworthy (this increment)

- [x] T015 [US3] Re-verify the constituent seed independently: regenerate it from the public
      constituents dataset and probe every symbol against the Yahoo chart endpoint.
- [x] T016 [US3] Store symbols in Yahoo form (`BRK-B`, not `BRK.B`) and record the provenance +
      regeneration recipe in the file's own `note`.
- [x] T017 [US3] Raise `BroadUniverseMaxIngestPerRun` 250 → 275 so a full rotation of the now
      503-name seed still lands inside `FreshnessMaxTradingDays`.
- [x] T018 [US3] Add the `Radar` config section to `FinanceSentry.API/appsettings.json` with
      `BroadUniverseEnabled: true` — the flag now has a runnable enable path, not just a default.
- [x] T019 [US3] Document the enable/disable path, the rotation budget and the seed's provenance in
      `docs/claude/app-state.md`.
- [x] T020 [US3] Test the constituent seam end to end: real `RadarUniverseService` composition → real
      `MarketStructureReader.GetUniverseStructuresAsync` → real `ScanNominationRules`, asserting a
      non-held, non-watchlisted constituent surfaces non-ETF-lens and wins the nomination.
- [x] T021 [US3] Guard the seed's symbol shape (no dotted tickers) so a hand-edit cannot reintroduce
      a permanently unresolvable member.
- [x] T022 [US3] Verify: `dotnet build` warning-free + `dotnet test FinanceSentry.sln`.

## US4 — prove the cycle against production behaviour (this increment)

- [x] T023 [US4] Extract the Radar half of the seam test into `BroadUniverseRadarFixture` so the
      cycle test drives the same composed, bar-backed universe instead of a second copy of it.
- [x] T024 [US4] Share `FakePositionCapSource` / `FakeMarketRegimeSource` from `OpportunityFakes`
      rather than keeping them private to `ScoreCandidateHandlerTests`.
- [x] T025 [US4] Drive the real `OpportunityScanJob` over the real `ScoreCandidateCommandHandler`
      and the real candidate repositories, asserting the persisted Scan-sourced candidate row and its
      score (fundamentals grade + RS window) read back through a fresh context.
- [x] T026 [US4] Cover the nightly cadence: a second cycle appends a score and dedups nomination
      reasons instead of duplicating the candidate.
- [x] T027 [US4] Verify: `dotnet build` warning-free + `dotnet test FinanceSentry.sln`, plus a
      mutation check that the new assertions bite on production behaviour.

## US5 — the two-stage funnel (#558 consolidated 2026-09-13)

- [x] T028 [US5] Add the `IScanShortlistSource` port to `FinanceSentry.Core/Interfaces`.
- [x] T029 [US5] Extract `PercentileRanks` out of `ScanNominationRules` so both funnel stages rank on
      one implementation.
- [x] T030 [US5] Add the pure `ScanShortlistRules`: surface ranking (quote percent change + street
      actions) bounded by the grading budget, then the quality re-rank capped at the shortlist size.
- [x] T031 [US5] Lift the EDGAR grading loop out of `OpportunityScanJob` into `FundamentalsGrading`
      so stage 1 and stage 2 grade identically.
- [x] T032 [US5] Implement `ScanShortlistService` over the constituent source, the batch quote read
      and the analyst-action feed; register it in `ResearchModule`.
- [x] T033 [US5] Add the `ScanShortlist*` options (size, grading budget, street weight and points,
      quality weight, action lookback and read limit).
- [x] T034 [US5] Point `RadarUniverseService` at the shortlist port instead of the constituent list.
- [x] T035 [US5] Drop the rotating whole-index ingestion: `IngestDailyBarsCommand.Schedule` and
      `RadarOptions.BroadUniverseMaxIngestPerRun` both go — the universe is the bound now.
- [x] T036 [US5] Tests: stage-1 rules (ranking, budget, street cap, missing-half, clamped weights)
      and the service (grades only the slate, target cuts do not count, every upstream may fail).
- [x] T037 [US5] Tests: `ShortlistScopedIngestionTests` (a cycle fetches book + lenses + shortlist
      over a 200-name index, and pays nothing when the flag is off) and `ShortlistStructureCostTests`
      (structure computed for at most K + |held| + |watchlist| with bars seeded for the whole index).
- [x] T038 [US5] Test: a name yesterday's shortlist carried and today's does not is de-activated.
- [x] T039 [US5] Verify: `dotnet build` warning-free + `dotnet test FinanceSentry.sln`.

## US6 — calibrate and re-prove the funnel (this increment)

- [x] T040 [US6] Calibrate `ScanTopDecileRsPercentile` and `ScanMaxNominationsPerRun` for a
      shortlist-sized universe — a top-decile cut over ~45 members is a different instrument than over
      500, and the nomination cap was set against the old width.
- [x] T041 [US6] Give the opportunity scan a LogOnly-style gate so a widened scan cannot fan out one
      Alert per top-tier candidate per user; precedent is Radar's `ScannerMode`.
- [x] T042 [US6] Re-point `BroadUniverseScanCycleTests` at a stage-1-composed universe so the
      acceptance proof runs *through* the funnel rather than past it, and assert the surviving
      candidate entered via the shortlist.
- [x] T043 [US6] Verify: `dotnet build` warning-free + `dotnet test FinanceSentry.sln`.

## US7 — what the funnel opened (beyond #558's clauses)

Neither task below is a `Done when` clause; both are holes US5 created and US6 could not close inside
one reviewable increment.

- [ ] T044 [US7] Close the candidate-staleness hole the funnel opens: a Scan candidate outside the
      book loses bar coverage the night its ticker drops off the shortlist, so its structure and
      invalidation monitoring go stale. Either union active-candidate tickers into the shortlist
      (raising the clause-3 bound to K + |held| + |watchlist| + |active candidates|, capped by the
      candidate TTL) or promote a nominated candidate to the watchlist so it rides the existing
      |watchlist| term — the second keeps the stated bound and needs owner agreement on the semantics.
- [ ] T045 [US7] Give stage 1 a genuinely batched quote read. `IMarketDataService.GetQuotesAsync`
      resolves a ticker at a time behind its cache, so the market-wide momentum signal costs ~500
      light requests a run. A batched quote endpoint (or reading the previous close from stored bars
      for names that have them) cuts that by an order of magnitude.
