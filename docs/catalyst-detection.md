# Catalyst detection

Stage 7 of the discovery funnel (catalysts beyond the ticker feed). Two jobs, no model call, both off-switchable in the
`Catalyst` configuration section: a daily scan of SEC 8-K filings over the [reference universe](reference-universe.md), so a
name outside the [coverage universe](coverage-universe.md) can come to the pipeline's notice on a filing, and a daily GDELT
article-volume reading per theme phrase, the stored attention measure. Owner ruling 2026-11-13: finding opportunities is the
pipeline's work; the owner decides buy or sell only.

## 8-K scan (`catalyst-detection`, Hangfire, daily 00:30 UTC; `CatalystDetectionJob`)

After the 00:00 opportunity scan, before the 01:10 coverage evaluation that reads the candidates it nominates.

1. **Universe.** The members of the reference lists whose kind is in `Catalyst:ListKinds` (default `index` and `theme`: the S&P 500, the Nasdaq-100 and the themes). The sector and industry lists hold every US-listed equity and are off by default. An empty universe (the weekly refresh has not run) is a logged skip, not a quiet day.
2. **Filings.** EDGAR full-text search (`efts.sec.gov`), one query per item in `Catalyst:Items` (`1.01` material definitive agreement, `2.02` results, `5.02` officer or director change, `8.01` other events; the order ranks nominations), over `Catalyst:LookbackDays` (3). Only form `8-K` counts (an `8-K/A` restates a filing already seen). A hit is kept only when its own item list carries a watched item and its ticker is in the universe; the query phrase merely narrows the search.
3. **Fallback.** The full-text endpoint is undocumented by the SEC. Any refusal, timeout, non-JSON body or body without `hits.hits` is a failure of that feed (never "no filings"), and the scan then reads the per-ticker submissions feed the app already uses (`SecEdgarService.GetRecentFilingsAsync`, with its ticker map and 30-minute cache; the submissions `items` column carries the item codes), at most `Catalyst:FallbackMaxTickers` (700) names a run from a rotating start, so a longer fallback still reaches every name. If neither feed can be read at all the job fails.
4. **Per user, once ever per filing** (identity: ticker and accession number, recorded in `catalyst_events`; a repeat run is a no-op):
   - a name **already covered** goes to the stage 4 event review (`ReviewDueRaiser.NoteCoverageOnly` and `CompleteAsync`, source `filing`, reference the accession: the same event identity the hourly filing watch uses, so the two never double-count). Disposition `Covered`.
   - a name that **left coverage** is skipped unless the filing is dated after the day of its last removal (`SkippedRemoved`); the stage 1 rule independently requires Buy readings dated after the removal, so a removed name needs newer evidence at both layers.
   - any other name is **nominated into the existing candidate path**: `ScoreCandidateCommand` with source `Scan` and the nomination reason `catalyst: 8-K item <item> (<meaning>)`. The tag is stable per item (re-nominations dedupe on the candidate) and is the first nomination reason on a new candidate, which is what the Ledger scoreboard reads as the screen tag, so catalyst nominations are graded as a group. Disposition `Nominated`.
   - at most `Catalyst:MaxNominationsPerRun` (3) names a run per user, most wanted item first, then newest filing, then ticker. A filing past the cap is not recorded; the next run, inside the lookback window, reconsiders it.
5. **A nomination is not a coverage add.** The scan writes no member and no journal row. The nightly stage 1 evaluation (`CoverageEvaluator`) adds the name only if its rules agree (a Scan candidate scoring Buy twice inside the window, ranked against the cap with the D1 correlation penalty); stage 4's reviewer applies the remove rules. This stage adds no rule of its own.

A candidate's alert behaviour is unchanged: `Opportunity:ScanAlertMode` (`LogOnly` at launch) gates the top-tier alert for a Scan candidate.

### Fair access

Every SEC request (full-text pages and fallback submissions reads) waits on one shared pacer set to `Catalyst:SecRequestsPerSecond` (5, half the SEC's 10 a second ceiling), sent on the existing `sec-edgar` client with its declared User-Agent (`FinanceSentry/1.0 (contact: ...)`, `SecEdgar:Contact`). A run is also bounded by `Catalyst:FullTextMaxPagesPerItem` (8 pages of 100 per item). Note: from the dev host used to build this the SEC returned HTTP 403 "undeclared automated tool" to the full-text endpoint, so the response shape is taken from community write-ups and the parser is defensive; the fallback exists for exactly this.

### Store (`research` schema, additive migration `M029_CatalystDetection`)

- `catalyst_events` - per user: `Ticker`, `Accession`, `Form`, `Items` (the watched items the filing carries), `FilingDate`, `Summary`, `DocumentUrl`, `Source` (`fulltext` or `submissions`), `DetectedAt`, `Disposition` (`Nominated`, `Covered`, `SkippedRemoved`), `Tag`, `Detail`. Unique per (user, ticker, accession); retained `Catalyst:EventRetentionDays` (120). Owner-scoped like the coverage tables.
- `theme_attention_days` - shared market data, no user: `Theme` (list key), `Phrase`, `Day`, `Articles`, `TotalArticles`, `Source`, `MeasuredAt`. Unique per (theme, phrase, day); retained `Catalyst:AttentionRetentionDays` (90).

Read with MCP `list_catalyst_events` and `get_theme_attention` ([mcp.md](mcp.md)).

## Theme attention (`theme-attention-refresh`, daily 00:10 UTC; `ThemeAttentionRefreshJob`)

GDELT DOC 2.0 `timelinevolraw`: article counts per day for a quoted phrase, English-language sources, last `Catalyst:GdeltTimespanDays` (14) days. The themes are the reference universe's (`theme:` lists, plus any theme declared in `ReferenceUniverse:Themes` the weekly refresh has not materialised yet); the words that mean each theme in the press are `Catalyst:ThemePhrases` (keyed by theme id; a theme with no phrase is logged and not measured). Requests are spaced `Catalyst:GdeltMinIntervalSeconds` (5) apart, the interval at which GDELT starts answering HTTP 429. A throttle, an empty body or text instead of JSON is a failed phrase, never a zero: the other phrases are still stored and the job then ends Failed. A re-read replaces the stored day (the newest day is partial).

The measure is a fact, not a rule: for each phrase the newest *complete* UTC day's count, the mean of up to 13 days before it (withheld below 5 stored days) and their ratio. `score_candidate` carries the readings of the themes the ticker is on as `evidence.attention` (`AttentionFact`); no sub-score, rule or add penalty reads it. Per the framework (3.4) high attention marks the crowded lane, a staging input rather than a reason to add. GDELT states no licence or SLA, so treat it as a soft signal.

## Options (`Catalyst`)

`Enabled` (true; false stops both jobs before any request), `AttentionEnabled` (true), `Items`, `ListKinds`, `LookbackDays` (3), `MaxNominationsPerRun` (3), `ScanHourUtc`/`ScanMinuteUtc` (0:30), `FullTextUrl`, `FullTextPageSize` (100), `FullTextMaxPagesPerItem` (8), `SecRequestsPerSecond` (5), `FallbackMaxTickers` (700), `GdeltUrl`, `GdeltTimespanDays` (14), `GdeltMinIntervalSeconds` (5), `AttentionHourUtc`/`AttentionMinuteUtc` (0:10), `AttentionRetentionDays` (90), `AttentionBaselineDays` (13), `AttentionMinBaselineDays` (5), `EventRetentionDays` (120), `MaxListedEvents` (200), `ThemePhrases`.

Rollback: set `Catalyst:Enabled` false (jobs stop, nothing else changes). Migration `M029` only creates the two tables above; its `Down` drops them.
