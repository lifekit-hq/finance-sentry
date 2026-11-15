# Coverage universe

"Our own index": the capped list of companies the pipeline keeps in view, maintained with no one's confirmation
(owner ruling 2026-11-13: he decides buy or sell; finding and curating are the pipeline's and Ledger's; Ledger proposes theses and the owner confirms, ruling 2026-11-15).
Stage 1 of the discovery funnel. It is distinct from the [reference universe](reference-universe.md), which is
the pool candidates are drawn from, not the list being watched.

## Store (`research` schema, Research migrations M026, M027 and M030)

- `coverage_members` - `Ticker`, `AddedAt`, `AddedReason` (`Held`, `Watchlist`, `Screen`, `Ledger`; `User` is reserved), `LastEventAt`, `LastScoreId`, `CorrelationToBook` (63-day, rule D1, null when the bars cannot support it), `RemoveAfter`. Unique per (user, ticker).
- `coverage_journal` - one row per add or remove with the rule that fired, the reason, a detail sentence, and the correlation and rank when a cap-bound add was ranked. Never rewritten.
- `coverage_events` (M027) - one row per deterministic hit on a covered name and what the review did about it (see Events). Unique per (user, ticker, source, reference); retained `Coverage:EventRetentionDays` (120).
- `coverage_memos` (M030) - one research memo per ranked coverage add (unique per user and journal entry), with the model, token counts, cost at the configured list price, `Status` (`Structured` or `Unstructured`), the verdict word and the body (see Research memos).
- `coverage_verdict_observations` - one scorecard verdict reading per (user, ticker, day), retained `Coverage:ObservationRetentionDays` (120). The scorecard is computed on demand, so "Buy twice" and "two consecutive Avoids" need a stored history; this is it.

## Rules (`CoverageRules`, pure; ids in `CoverageRuleIds`)

Add:

- `held` - an open equity holding. `watchlist` - on the watchlist. Both pinned: they join even over the cap.
- `ledger_nomination` - an Active Ledger-sourced candidate with a decision note (the creation event's note). Ranked.
- `buy_twice_in_window` - an Active Scan or Ledger candidate read Buy or StrongBuy on two distinct days within 14 days. Ranked.

A ranked add is withheld when a remove rule already matches the same facts (no add one night, remove the next).

Remove (never for a held, watchlisted or thesis-carrying name):

- `avoid_twice` - the two latest readings are Avoid. `candidate_expired` - the name's candidates are all expired. `no_event` - 60 days since the later of its last event and when it joined, and no dated catalyst (thesis catalyst or earnings date within 60 days). A failed earnings lookup never counts as "no catalyst".

A removed name rejoins by a ranked rule only on evidence created after its last journalled removal: Buy readings on later days, or a Ledger nomination whose candidate was created after the removal. Held and watchlist adds need no cutoff (current state, protected from every remove rule).

## Cap and rank

`Coverage:MaxMembers` (30 at launch in `appsettings.json`, 100 by default). Existing members are never displaced. Ranked adds fill the free slots in `rank` order: the mean of the scorecard's structure and fundamentals scores less the rule-D1 correlation penalty (`CorrelationFact`, the same one the opportunity add rule uses), so a name that moves with the owner's DRAM/SOXX concentration ranks down. Pinned adds are not capped; a pinned add over the cap is journaled as such, and a ranked add that does not fit is simply retried the next night.

## Evaluation

`coverage-evaluation` (Hangfire, daily `Coverage:EvaluationHourUtc:EvaluationMinuteUtc`, 01:10 UTC; `CoverageEvaluationJob`) - deterministic, no model call. Per user: record verdicts, refresh each member (last event, last score, correlation, `RemoveAfter`), apply the remove rules, then the adds. A failure for one user does not stop the rest; the run then fails so Hangfire records it. Event sources for `LastEventAt` are the thesis journal (excluding the weekly `Snapshot`), candidate scores and the events below.

## Events on coverage names

The look-ahead detectors read the coverage set too (`LookaheadUniverse.CoverageTickersAsync`; held names first, crypto assets out). A coverage name that is not otherwise in a detector's alert universe earns no alert of its own - no `FilingLanded`, `EarningsAhead` or `NewsCluster` - only the steps below; a held, thesis or watchlist name keeps its alert as before.

- `FilingWatchJob` (hourly), `EarningsAheadJob` (an earnings date within 3 days), `EarningsPassedJob` and `NewsMaterialityJob` (a cluster) note each hit on a covered name. `NewsIngestionJob` reads the news: the book (watchlist, then thesis, then held names) keeps its 60 places, and coverage names those places do not read (a covered name cut from the book's tail included) are read on top in rotating slices of `Coverage:NewsCoverageTickersPerRun` (40) every `Coverage:NewsRotationMinutes` (30), so 100 names are all read every three runs.
- Once the user's pass ends (`ReviewDueRaiser.CompleteAsync` -> `CoverageEventReviewer`), each hit not seen before (ticker, source, reference) is recorded as a `coverage_events` row, moves the member's `LastEventAt` to now, re-scores the member's Active candidate (the `score_candidate` path: market structure and fundamentals read, no model) and runs the remove rules through `CoverageEvaluator.ReviewTickersAsync` (the same `CoverageRules`, same journal). The adds stay with the nightly run and its removal cutoff: a name a review removed rejoins only on evidence newer than the removal.
- The review-due notice (`CandidateReviewDue` for an Active candidate, `ThesisReviewDue` for a thesis) is still raised by the filing, earnings-passed and news-cluster detectors, once per subject, source and reference; the earnings-ahead hit is noted for coverage only and raises none. A hit's `ReviewNotice` records only a notice actually raised. A covered name with neither subject (held without a thesis, watchlisted) is recorded and keeps its `LastEventAt` but raises no notice and is not re-scored: it has no candidate, and a review would create one.
- A repeat of a hit already recorded returns before anything runs. A rescore that fails is recorded on the row (`Rescored` false) and the rules still run. Nothing here calls a model.

The disposition digest is `list_coverage_events(sinceUtc?, limit?)` ([mcp.md](mcp.md)): the events since the time (newest first, `total` counts all, as do the `removed` and `rescored` counts), each with its `disposition` (`Kept`, `Removed`, `Pending`), and the coverage adds and removes in the same span, so the pulse reads one list instead of being woken per event.

## Screens (stage 5): where nominations come from without the owner

Beside the price-and-attention scan (`opportunity-scan`, 00:00 UTC), three deterministic screens look at the [reference universe](reference-universe.md) (`Screens:ListKeys`, default `sp500`, `nasdaq100`, `theme:*`) and nominate into the same candidate path: `score_candidate`'s `ScoreCandidateCommand`, source `Scan`, so the Scan alert gate (`LogOnly`), the correlation fact and the coverage rules apply unchanged. `screen-nomination` (Hangfire, daily `Screens:RunHourUtc:RunMinuteUtc`, 23:30 UTC - after Radar's 23:00 compute, before the scan and the 01:10 evaluation; `ScreenNominationJob` -> `ScreenNominator`). No model call.

| Screen (tag) | Reads | Fires when | Evidence date |
|---|---|---|---|
| `screen: fundamentals` | EDGAR facts through the fundamentals chain (`SecEdgarService`), a rotating daily slice of `Screens:Fundamentals:BatchSize` (40) names | latest fiscal year: revenue growth >= 15%, operating cash flow margin >= 15%, capex intensity <= 15%, equity > 0 (`FundamentalsScreenRules`; a measure that cannot be computed fails the screen, never defaults) | fiscal year end + `FilingLagDays` (75) |
| `screen: relative strength` | stored bars of the reference-list names Radar tracks (`IMarketStructureReader`) | `ScanNominationRules` with the `Opportunity` thresholds, ranked among those names only; needs `MinPoolSize` (20) fresh, non-ETF-lens names with bars | the run time (the cooldown is the guard) |
| `screen: dated catalyst` | Finnhub `/calendar/earnings` for the next `LookaheadDays` (10), one request a run, `AnalystSources:Finnhub` key (no key: skipped) | a reference-list name reports inside the window | the day the date entered the window |

The tag is a nomination reason on the candidate; the Ledger scoreboard attributes a `Screen` coverage add (`CoverageSourceGradeDto.ScreenTag`) to the first reason that is a screen tag (`ScreenTags.IsScreenTag`), else the first reason, so a candidate the scan or the owner created and a screen later hit is still credited to the screen and each screen is graded against the S&P 500 on its own. No new column, enum value or table: nothing to migrate or roll back.

Per user, a hit is not nominated when the name is already covered, when the user already has an Active candidate carrying the tag (a re-run changes nothing), or when the user's coverage removed it and the evidence is not newer than the removal (`ScreenSuppression`; newer evidence is also held back for `Screens:RemovedCooldownDays`, 30). Caps: `MaxNominationsPerRun` per screen (3, 4, 3), `MaxNominationsPerUserPerRun` over all (10), and the room left under `Coverage:MaxMembers`. A screen that cannot run (the EDGAR source down, the calendar erroring) fails the job after the others have nominated; a missing Finnhub key or a too-small bar pool is a logged skip.

## Held names: the soft cap (owner call D2)

The book is meant to hold at most 12 names (`Coverage:HeldSoftCap`). It is a soft cap: nothing is blocked and no name is removed. `list_coverage` reports `heldCount` (open equity holdings; crypto is out), `heldSoftCap` and `heldOverSoftCap`, and the nightly evaluation logs a warning when the count passes it. Held names always join coverage, so they can take the coverage list over `MaxMembers`; the other 88 names are the funnel's to follow.

## Research memos on entrants (stage 6): the funnel's one model call

For a name a ranked rule added (`Screen` or `Ledger`; a held or watchlisted add is the owner's own act and gets no memo), `coverage-memos` (Hangfire, daily `Coverage:MemoHourUtc:MemoMinuteUtc`, 01:40 UTC - after the 01:10 evaluation; `CoverageMemoJob` -> `CoverageMemoWriter`) makes one single-turn call, no tools, over evidence the pipeline already stored: the journal row that added the name, its deterministic scorecard, its latest coverage events and its correlation to the book. The reply is stored in `coverage_memos` as `business`, `whyNow`, `falsifiers` (metric ones in the thesis-trigger vocabulary of `ThesisMetric`, so a later thesis can adopt them as triggers; the rest narrative), `crowding`, `correlationToBook`, a `Buy`, `Watch` or `Avoid` verdict and `caveats`. A reply that is not that shape is kept as raw text and still counts against the budget. The call goes through the Core port `IMemoLanguageModel` (implemented in the Agent module on its Anthropic credentials; Research takes it optionally, so a host without the Agent module makes no call).

Spend is capped three ways, all checked before each call and none of them a prompt:

- `Coverage:MemoMode` - `LogOnly` (the default, owner call D3 = a: memos are written and stored; nothing acts on them, and the two caps below bound the spend) or `Off` (no call, no spend, whatever else is set; the owner can set it any time).
- `Coverage:MaxMemosPerDay` (3) across everyone, counted from the rows written that UTC day. Entrants over it wait; a name stays eligible for `Coverage:MemoLookbackDays` (7), best rank first.
- `Coverage:MemoWeeklyBudgetUsd` (25, owner call D3) over a rolling seven days. A call is made only when the recorded cost of the last seven days plus the call's worst case (the prompt's characters over three, plus `MemoMaxOutputTokens`, at `MemoInputUsdPerMillionTokens` 2 and `MemoOutputUsdPerMillionTokens` 10, the Sonnet list price) fits under it; the run then stops. The prompt is cut at `MemoMaxInputChars`. A failed provider call stops the run and fails the job; the entrant is retried the next night.

Paper-only: no code reads a memo to place, size or change a position or to move a name in or out of coverage; the verdict is a word on a stored row. The Ledger scoreboard already grades every coverage add against the S&P 500 (`get_ledger_scoreboard`), and for the first 30 days nobody sizes on a memo. `explain_coverage` returns the newest memo (`memo`, `paperOnly: true`); `list_coverage` marks members that have one (`latestMemoAt`).

## Consumers

- `ICoverageUniverseReader` (Core port; freshest activity first, then by ticker) - the portfolio-construction universe screen reads it instead of the old hard-coded list (`PortfolioConstructionOptions.CuratedUniverse` is gone).
- MCP `list_coverage`, `explain_coverage(ticker)` and `list_coverage_events` ([mcp.md](mcp.md)).
- A coverage name carries a candidate scorecard plus a decision note, not a Ledger thesis: a thesis exists only for a held name (owner ruling 2026-11-15). A thesis already on file still protects the name from removal.

## Catalyst nominations

A name outside coverage can reach it on an 8-K: the daily [catalyst scan](catalyst-detection.md) nominates it into the candidate path with a `catalyst: 8-K item ...` tag (source `Scan`), and the rules above decide the add; a covered name's filing goes through the stage 4 review. The scan adds no member and no rule, and a removed name is not re-nominated by a filing dated on or before its removal day.
