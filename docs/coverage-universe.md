# Coverage universe

"Our own index": the capped list of companies the pipeline keeps in view, maintained with no one's confirmation
(owner ruling 2026-11-13: he decides buy or sell; finding, thesising and curating are the pipeline's and Ledger's).
Stage 1 of the discovery funnel. It is distinct from the [reference universe](reference-universe.md), which is
the pool candidates are drawn from, not the list being watched.

## Store (`research` schema, Research migration M026)

- `coverage_members` - `Ticker`, `AddedAt`, `AddedReason` (`Held`, `Watchlist`, `Screen`, `Ledger`; `User` is reserved), `LastEventAt`, `LastScoreId`, `CorrelationToBook` (63-day, rule D1, null when the bars cannot support it), `RemoveAfter`. Unique per (user, ticker).
- `coverage_journal` - one row per add or remove with the rule that fired, the reason, a detail sentence, and the correlation and rank when a cap-bound add was ranked. Never rewritten.
- `coverage_verdict_observations` - one scorecard verdict reading per (user, ticker, day), retained `Coverage:ObservationRetentionDays` (120). The scorecard is computed on demand, so "Buy twice" and "two consecutive Avoids" need a stored history; this is it.

## Rules (`CoverageRules`, pure; ids in `CoverageRuleIds`)

Add:

- `held` - an open equity holding. `watchlist` - on the watchlist. Both pinned: they join even over the cap.
- `ledger_nomination` - an Active Ledger-sourced candidate with a decision note (the creation event's note). Ranked.
- `buy_twice_in_window` - an Active Scan or Ledger candidate read Buy or StrongBuy on two distinct days within 14 days. Ranked.

A ranked add is withheld when a remove rule already matches the same facts (no add one night, remove the next).

Remove (never for a held, watchlisted or thesis-carrying name):

- `avoid_twice` - the two latest readings are Avoid. `candidate_expired` - the name's candidates are all expired. `no_event` - 60 days since the later of its last event and when it joined, and no dated catalyst (thesis catalyst or earnings date within 60 days). A failed earnings lookup never counts as "no catalyst".

## Cap and rank

`Coverage:MaxMembers` (30 at launch in `appsettings.json`, 100 by default). Existing members are never displaced. Ranked adds fill the free slots in `rank` order: the mean of the scorecard's structure and fundamentals scores less the rule-D1 correlation penalty (`CorrelationFact`, the same one the opportunity add rule uses), so a name that moves with the owner's DRAM/SOXX concentration ranks down. Pinned adds are not capped; a pinned add over the cap is journaled as such, and a ranked add that does not fit is simply retried the next night.

## Evaluation

`coverage-evaluation` (Hangfire, daily `Coverage:EvaluationHourUtc:EvaluationMinuteUtc`, 01:10 UTC; `CoverageEvaluationJob`) - deterministic, no model call. Per user: record verdicts, refresh each member (last event, last score, correlation, `RemoveAfter`), apply the remove rules, then the adds. A failure for one user does not stop the rest; the run then fails so Hangfire records it. Event sources for `LastEventAt` are the thesis journal (excluding the weekly `Snapshot`) and candidate scores; event watching on coverage names is a later stage.

## Consumers

- `ICoverageUniverseReader` (Core port; freshest activity first, then by ticker) - the portfolio-construction universe screen reads it instead of the old hard-coded list (`PortfolioConstructionOptions.CuratedUniverse` is gone).
- MCP `list_coverage` and `explain_coverage(ticker)` ([mcp.md](mcp.md)).
- A coverage name may carry a Ledger thesis, not only a scorecard; an active thesis protects it from removal.
