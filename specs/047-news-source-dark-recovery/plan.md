# Implementation Plan: News Source Dark Recovery

**Branch**: `047-news-source-dark-recovery` | **Date**: 2026-09-07 | **Spec**: [spec.md](./spec.md)

## Architecture Decisions

- **Recovery is a separate recurring Hangfire job, not a branch inside `NewsIngestionJob`.** The
  ingestion job's contract is "iterate the enabled registry every 30 minutes"; probing retired
  sources runs on a different cadence (6-hourly) and has different health semantics (a failure is
  expected and silent). Precedent for a second, sparser job over the same table is
  `NewsSourceSeedJob` (`Cron.Daily(2)`) alongside `research-news-tickers` (`*/30`).

- **Back-off is the job cadence, not a persisted timestamp.** A per-source `LastProbeAt` column
  would need a hand-written migration (no `dotnet-ef` here) to express something the schedule
  already expresses: probe every disabled source once per run, run four times a day. Sources number
  in the handful, so there is nothing to page through.

- **Disabled unambiguously means auto-retired.** `Enabled = false` is written in exactly one place
  (`NewsSourceHealthTracker.RecordFailure`); no command, API or MCP tool lets a user switch a source
  off. So reviving every disabled source that fetches cannot override an intentional choice. If a
  user-facing disable is ever added, it needs a distinct field (or a `RetiredAt`), and this job must
  gate on it — recorded here so that change does not silently re-enable what a user turned off.

- **The per-source fetch/map is extracted into `NewsSourceFetcher`.** Both jobs need "given a
  `NewsSource`, fetch its articles through the right adapter, stamp source/thesis/hash". Leaving it
  private in `NewsIngestionJob` would force the recovery job either to duplicate it or to inherit
  the ingestion job's seven unrelated dependencies (brokerage, crypto, banking, alerts). The
  extraction is a straight move of `FetchRssAsync`/`FetchPageAsync`/`HashContent`/`Trim` plus the
  tagging line; `NewsIngestionJob` keeps orchestration and health/alerting. It lives in
  `Infrastructure/Sources/` beside the page adapters it dispatches to and the
  `NewsSourceParseException` it throws, not in `Application/Services/`.

- **A revived source can flap, and that is the alert path's job to damp.** Revival resets
  `ConsecutiveFailures`, which re-arms the fire-once alert in `NewsSourceHealthTracker`: a source
  that recovers and breaks again will alert again ~6 h later. That is the correct signal (the source
  genuinely flapped), and it is bounded outside this feature —
  `AlertGeneratorService.GenerateSyncFailureAlertAsync` returns early if a `SyncFailure` alert is
  already active and otherwise honours a 12 h silence window (news-source alerts carry no
  `accountId`, so both checks are per user). No probe-side suppression is added on top of a dedup
  that already exists.

- **No `INewsSourceFetcher` port.** It is a composition of two existing ports inside one module, with
  one implementation; tests build a real fetcher over fake page sources. The repo reserves interfaces
  for substitution points that actually vary (`INewsPageSource`, `IMarketNewsService`).

- **A failed probe rewrites `LastFailureReason` but not the counter.** `ConsecutiveFailures` means
  "failures while in service" — it drives the alert and disable thresholds, and inflating it from a
  background probe would make the number meaningless (a source dark for a month would read 120).
  The reason is worth refreshing: the stored one is from the day the source retired, while a
  diagnosis needs today's error.

- **Revival = `ClearFailures` then `RecordSuccess`.** Two intent-revealing tracker calls rather than
  a third tracker method or hand-assigned fields: the cause is void *and* we just observed a real
  success.

## Story Slice [US1] — Probe and revive retired sources

### Files touched

- `backend/src/FinanceSentry.Modules.Research/Infrastructure/Sources/NewsSourceFetcher.cs` — new;
  fetch + map for one source (moved out of `NewsIngestionJob`)
- `backend/src/FinanceSentry.Modules.Research/Infrastructure/Jobs/NewsIngestionJob.cs` — delegates to
  the fetcher; loses the private fetch/map helpers and the `INewsPageSource` dependency (it keeps
  `IMarketNewsService` for the ticker and Fed ingestion paths)
- `backend/src/FinanceSentry.Modules.Research/Infrastructure/Jobs/NewsSourceRecoveryJob.cs` — new
- `backend/src/FinanceSentry.Modules.Research/Domain/Repositories/INewsSourceRepository.cs` +
  `Infrastructure/Persistence/Repositories/NewsSourceRepository.cs` — `ListDisabledAsync`
- `backend/src/FinanceSentry.Modules.Research/ResearchModule.cs` — DI for the fetcher and the job,
  plus the `research-news-source-recovery` recurring registration
- `backend/tests/FinanceSentry.Modules.Research.Tests/Jobs/NewsSourceRecoveryJobTests.cs` — new
- `backend/tests/FinanceSentry.Modules.Research.Tests/Companion/CompanionFakes.cs` —
  `ListDisabledAsync` on `FakeNewsSourceRepository`, plus fakes for `INewsRepository` and
  `INewsPageSource` if not already present

### Constraints discovered

- `FakeNewsSourceRepository` hands back **copies** (mirroring `AsNoTracking()`), so the job must
  persist through `UpdateAsync` — a test that asserts on the object it handed the job would pass
  even if nothing was written.
- `NewsIngestionJob` currently maps page candidates with `Trim(...)` caps (500/2000/4000) that mirror
  the column widths; those move with the fetcher unchanged (as named constants).
- `RssMarketNewsService.FetchFeedArticlesAsync` **swallows** network/parse failures and returns an
  empty list, so an RSS source never fails the health tracker at all — the disabled set is in
  practice Page sources plus anything that failed on insert. A probe of an RSS source therefore
  always "succeeds"; that is the same verdict the ingestion sweep reaches for the same feed, and
  making the probe stricter than ingestion would revive sources into a loop. Left as is.
- `dotnet test FinanceSentry.sln -m:1` is required in this 2 CPU / 4 GB sandbox; the default parallel
  run is OOM-killed. Restore before `--no-restore` on a fresh checkout.
- Backend-only diff plus `specs/` — the husky pre-commit hook's frontend gate does not fire.

### [US2] (not this slice) — recurring dark-source alerting

Would touch `NewsSourceRecoveryJob` (or a sibling) and the alerts path (`IAlertGeneratorService`,
`GenerateSyncFailureAlertAsync`) plus a silence window, in the shape hygiene sentinels (044) use for
dedup. No schema change needed if the window is derived from `LastSuccessAt`.
