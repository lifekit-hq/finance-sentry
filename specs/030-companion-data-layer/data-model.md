# Data Model: Companion-Mode Data Layer

**Feature**: 030-companion-data-layer | **Migration**: `M008_CompanionDataLayer` on `ResearchDbContext` (schema `research`)

## New Tables

### `analyst_actions`

Global market data (no `UserId`) — precedent: `news_articles`, `quote_cache`.

| Column | Type | Notes |
|---|---|---|
| `Id` | uuid PK | `gen_random_uuid()` |
| `Ticker` | varchar(12), indexed | normalized upper-case |
| `Firm` | varchar(120) | research firm name, normalized (trimmed, canonical casing) |
| `ActionType` | varchar(20) | enum-as-string: `Upgrade`, `Downgrade`, `Initiate`, `TargetChange`, `Reiterate`, `TopIdea` |
| `PriorRating` | varchar(40) NULL | e.g. "Equal-Weight" |
| `NewRating` | varchar(40) NULL | |
| `PriorTarget` | numeric(18,4) NULL | |
| `NewTarget` | numeric(18,4) NULL | |
| `ActionDate` | date | the street event date, not retrieval date |
| `Source` | varchar(40) | `marketbeat` \| `yahoo` |
| `SourceUrl` | text NULL | |
| `IngestedAt` | timestamptz | |

**Indexes**: unique `(Ticker, Firm, ActionDate, ActionType)` — logical dedup identity (FR-003); non-unique `(ActionDate desc)` for date-range queries; `(Ticker, ActionDate desc)`.

**Dedup rule**: on conflict with an existing row, keep/merge the *richer* record (fill NULL target/rating fields from the incoming row); never insert a duplicate (spec edge case: two sources, rounded targets).

### `analyst_universe_members`

Pattern: `radar_universe_members` (compose seed ∪ live sets, deactivate on departure).

| Column | Type | Notes |
|---|---|---|
| `Id` | uuid PK | |
| `Ticker` | varchar(12), unique index | |
| `Reason` | varchar(20) | enum-as-string: `IndexConstituent`, `Holding`, `Watchlist`, `Candidate`, `Manual` |
| `Active` | boolean | departed members flip false, rows retained |
| `AddedAt` | timestamptz | |

Seed: checked-in S&P 500 constituent JSON resource + sync from holdings/watchlist/candidates on each ingestion run.

### `news_sources`

| Column | Type | Notes |
|---|---|---|
| `Id` | uuid PK | |
| `Name` | varchar(80) | display name, e.g. "TrendForce Press Center" |
| `Kind` | varchar(10) | enum-as-string: `Rss`, `Page` |
| `Url` | text | feed or page URL |
| `Keywords` | jsonb NULL | `List<string>`, `StringListComparer` pattern; filter for tagging/inclusion |
| `ThesisId` | uuid NULL, FK → `theses.Id` | NULL = market-wide default source |
| `Enabled` | boolean | |
| `ConsecutiveFailures` | int | reset on success; alert at ≥ 2 (FR-009) |
| `LastSuccessAt` | timestamptz NULL | |
| `LastFailureReason` | text NULL | |
| `CreatedAt` | timestamptz | |

Seed rows (in migration or startup seeder): market-wide defaults (Yahoo top stories RSS, MarketWatch top stories RSS) + TrendForce press page registered to the seeded DRAM thesis (matched by ticker at seed time; skip gracefully if thesis absent).

### `valuation_snapshots`

Persisted on every computation to accrue self-built history (R3).

| Column | Type | Notes |
|---|---|---|
| `Id` | uuid PK | |
| `Ticker` | varchar(12) | |
| `CapturedAt` | timestamptz | |
| `Price` | numeric(18,4) | |
| `TrailingPe` | numeric(12,4) NULL | NULL = unavailable, never 0 |
| `ForwardPe` | numeric(12,4) NULL | |
| `EvToEbitda` | numeric(12,4) NULL | |
| `DividendYield` | numeric(8,6) NULL | |
| `ConsensusTarget` | numeric(18,4) NULL | |
| `IsStale` | boolean | price older than the last trading session plus a margin, not market state (#1529) |

**Index**: `(Ticker, CapturedAt desc)`.

## Modified Tables

### `news_articles` (M008 alter)

- Add `ThesisIds` jsonb NULL — `List<Guid>` (converter + comparer per existing `Tickers` pattern). Tagged at ingestion: source-registered thesis and/or keyword match (R6).

## Code-only changes (no migration)

- `CandidateSource` enum: add `Ledger` (stored as string — safe).
- `AlertType`: reuse existing `SyncFailure` const for ingestion-source failures (R8); no new type.

## Domain model additions (Research module)

- `AnalystAction` entity + `AnalystActionType` enum.
- `AnalystUniverseMember` entity + `UniverseReason` enum.
- `NewsSource` entity + `NewsSourceKind` enum.
- `ValuationSnapshot` entity.
- `ValuationSnapshotResult` DTO (computed view): current metrics + `FiveYearAvgTrailingPe?` + `HistoryWindowYears` + per-metric availability flags + peer comparison rows + `ImpliedUpsidePct?` + `IsStale`.
- Domain interfaces (constitution Principle I — no concrete adapter references):
  - `IAnalystActionsSource` — `Task<IReadOnlyList<AnalystActionRecord>> FetchAsync(...)`; implementations `MarketBeatAnalystActionsSource`, `YahooAnalystActionsSource` in Infrastructure.
  - `IValuationDataService` — current-metrics fetch (Yahoo quoteSummary) behind an interface; trailing-P/E history composer uses existing `ISecEdgarService` + `IMarketDataService`.
  - `INewsPageSource` — for `Page`-kind sources (TrendForce), returning article candidates for the shared pipeline.

## Relationships

- `news_sources.ThesisId` → `theses.Id` (SET NULL on thesis delete).
- `analyst_actions` intentionally has **no** FK to universe (MarketBeat sweep stores actions for any ticker; the universe governs per-ticker Yahoo ingestion and the "in universe?" query distinction).
- `valuation_snapshots` standalone; queried by ticker.

## State transitions

- `NewsSource.ConsecutiveFailures`: 0 →(failure)→ n; n ≥ 2 → sync-failure alert (deduped); →(success)→ 0.
- `AnalystUniverseMember.Active`: true ⇄ false via nightly sync (never deleted).
- `OpportunityCandidate`: unchanged flows; new `Source=Ledger` value participates in existing Active → Promoted/Rejected/Expired lifecycle.
