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

- [ ] T010 [US2] Join the EDGAR fundamentals grade with the 018 structure/RS rank into a combined
      scan score.
- [ ] T011 [US2] Rank the whole ingested universe by that score in the nomination path, keeping the
      per-run nomination cap.
- [ ] T012 [US2] Tests: a non-held, non-watchlisted constituent can be nominated; ranking order and
      cap are deterministic.
