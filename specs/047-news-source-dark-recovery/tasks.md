# Tasks — Spec 047: News Source Dark Recovery

## [US1] Probe and revive retired sources

- [x] T001 `NewsSourceFetcher` — extract per-source fetch + article mapping (RSS feed vs page adapter,
      title/url/summary trimming, content hash, thesis tagging) out of `NewsIngestionJob` into one
      reusable service. Placed in `Infrastructure/Sources/` beside the page adapters it dispatches to
      and the `NewsSourceParseException` it throws
- [x] T002 `NewsIngestionJob` — delegate to `NewsSourceFetcher`, keeping orchestration, health
      tracking and alerting; drop the dependencies it no longer uses
- [x] T003 `INewsSourceRepository` + `NewsSourceRepository` — add `ListDisabledAsync`
- [x] T004 `NewsSourceRecoveryJob` — probe each disabled source; revive + persist articles on
      success, refresh only `LastFailureReason` on failure, never alert, never abort the run
- [x] T005 `ResearchModule` — register the fetcher and the job, and schedule
      `research-news-source-recovery` on a 6-hourly cron offset from the other news jobs
- [x] T006 `CompanionFakes` — `ListDisabledAsync` on `FakeNewsSourceRepository`; fakes for
      `INewsRepository`, `IMarketNewsService` and `INewsPageSource` usable by both jobs' tests
- [x] T007 `NewsSourceRecoveryJobTests` — revival (enabled, counter 0, reason cleared, `LastSuccessAt`
      stamped, persisted through `UpdateAsync`), articles inserted and thesis-tagged, still-failing
      source stays disabled with an un-inflated counter and a refreshed reason, one throwing source
      does not block the next, and an enabled source is never probed
- [x] T008 `NewsSourceFetcherTests` — RSS vs page dispatch, unhandled page URL throws
      `NewsSourceParseException`, over-long fields trimmed to the column caps, thesis tagging applied

### Not written as a test

- Acceptance scenario 6 (a failed probe raises no alert) is structural, not conditional:
  `NewsSourceRecoveryJob` takes no `IAlertGeneratorService`, so there is no branch to assert on. A
  test with a never-called alert fake would assert the constructor, not behaviour.

### Refactor taken along the way

- [x] `FakeNewsRepository` lifted out of `SearchMarketNewsQueryTests` into the shared `CompanionFakes`
      and given capture semantics (`Inserted`) — the private copy discarded what it was handed, so an
      ingestion path that persisted nothing would still have looked correct.

## Verification

- [x] `dotnet build FinanceSentry.sln -c Release -m:1` — Build succeeded, no warnings
- [x] `dotnet test FinanceSentry.sln --no-build -c Release -m:1` — **1489 passed, 0 failed, 8 skipped**
      across 11 assemblies (`FinanceSentry.Modules.Research.Tests`: 255 passed / 2 skipped)

## Deferred

- [ ] [US2] Recurring dark-source alerting (see plan.md) — a source that stays dark past a grace
      window re-announces itself instead of relying on the one-shot disable alert.
- [ ] SC-004: after deploy, confirm `research-news-source-recovery` appears in the Hangfire dashboard
      and logs a probe outcome per disabled source. The sandbox cannot reach the VPS.
