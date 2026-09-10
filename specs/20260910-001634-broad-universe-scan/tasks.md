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
