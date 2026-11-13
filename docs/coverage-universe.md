# Coverage universe

"Our own index": the capped list of companies the pipeline keeps in view, maintained with no one's confirmation
(owner ruling 2026-11-13: he decides buy or sell; finding, thesising and curating are the pipeline's and Ledger's).
Stage 1 of the discovery funnel. It is distinct from the [reference universe](reference-universe.md), which is
the pool candidates are drawn from, not the list being watched.

## Store (`research` schema, Research migrations M026 and M027)

- `coverage_members` - `Ticker`, `AddedAt`, `AddedReason` (`Held`, `Watchlist`, `Screen`, `Ledger`; `User` is reserved), `LastEventAt`, `LastScoreId`, `CorrelationToBook` (63-day, rule D1, null when the bars cannot support it), `RemoveAfter`. Unique per (user, ticker).
- `coverage_journal` - one row per add or remove with the rule that fired, the reason, a detail sentence, and the correlation and rank when a cap-bound add was ranked. Never rewritten.
- `coverage_events` (M027) - one row per deterministic hit on a covered name and what the review did about it (see Events). Unique per (user, ticker, source, reference); retained `Coverage:EventRetentionDays` (120).
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

## Consumers

- `ICoverageUniverseReader` (Core port; freshest activity first, then by ticker) - the portfolio-construction universe screen reads it instead of the old hard-coded list (`PortfolioConstructionOptions.CuratedUniverse` is gone).
- MCP `list_coverage`, `explain_coverage(ticker)` and `list_coverage_events` ([mcp.md](mcp.md)).
- A coverage name may carry a Ledger thesis, not only a scorecard; an active thesis protects it from removal.

## Catalyst nominations

A name outside coverage can reach it on an 8-K: the daily [catalyst scan](catalyst-detection.md) nominates it into the candidate path with a `catalyst: 8-K item ...` tag (source `Scan`), and the rules above decide the add; a covered name's filing goes through the stage 4 review. The scan adds no member and no rule, and a removed name is not re-nominated by a filing dated on or before its removal day.
