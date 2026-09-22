# Feature Specification: Events Module (calendar, fired-events feed, verdicts)

**Feature Branch**: `fm/fs-events-module`

**Created**: 2026-09-22

**Status**: Draft (two open decisions, see "Open decisions" - routed to firstmate before build)

**Origin**: Ledger heartbeat design (second mate `data/ledger-heartbeat-s1/report.md`, ruled by
Denys 2026-09-17) - the last piece of the set that began with the five detectors now on main:
earnings-ahead (#655), filing-landed (#656), news-cluster (#660), intraday-move (#665) and
budget-breach (#666). Captain's ask, verbatim intent: *upcoming events (earnings dates,
ex-dividend dates, filings due, the macro calendar, thesis trigger dates), a fired-events feed
that shows what actually happened and what Ledger made of it - its verdict, or its silence -
an API, an MCP tool for Ledger, and a calendar page in the SPA. finance-sentry owns this. Ledger
reads it. The arrow stays one-way.*

## Context

What exists today, and what the module consumes rather than rebuilds:

| Concern | Exists | Where |
|---|---|---|
| Earnings + ex-dividend dates | live Yahoo fetch, 6h in-process cache, never persisted | `IEarningsCalendarService` / `GetEarningsCalendarQuery` (Research) |
| Macro calendar | persisted `research.macro_events` (2026 FOMC/CPI/NFP/ECB seed) | `IMacroCalendarService.QueryAsync` (Research) |
| Thesis trigger dates | `InvestmentThesis.Catalysts[]` (`Date`, `Event`) as jsonb, inert - nothing reads them by date | `IThesisRepository` (Research) |
| Filings **landed** | EDGAR submissions feed, hourly detector | `ISecEdgarService.GetRecentFilingsAsync`, `FilingWatchJob` |
| Filings **due** | nothing - no due-date concept anywhere | - |
| Fired events | `alerts.alerts` rows of the detector types; `companion.companion_events` outbox row per material alert (`DedupKey = alert:{id}`, disposition lifecycle) | Alerts, Companion |
| The reader's verdict | **nothing** - `acknowledge_companion_events` flips `Delivered`, textless; Ledger's reasoning lives on its own VPS disk (`event-log.jsonl`) | Companion |

Two rules bind the design. Constitution Principle VII: MCP is the reader's only tool surface, the
hook carries ids only, no surface is named after Ledger. The detector-set rule: this module does
not modify any detector, alert type, or `AlertGeneratorService`; where a shared method needs new
behaviour the caller chooses it and today's behaviour stays the default.

---

## Open decisions (block the build, not the spec)

**D1 - how a verdict gets into Finance Sentry.** Nothing records what the reader made of an
event. The feed can show *silence* today (acknowledged, nothing said) but cannot show a *verdict*
without a write path. Options:

- (a) **Recommended.** A new write tool `record_event_verdict(eventId, verdict, notified)` owned by
  this module, keyed by the companion event id the reader already holds from the wake payload /
  `get_pending_companion_events`. Silence stays derivable: acknowledged with no verdict recorded.
  Cost: the reader's wake-turn contract (hook `messageTemplate` / persona, lifekit-stack config,
  outside this repo) must be updated to call it after judging; until then every fired event reads
  "silent", which is true and visible.
- (b) No write path: the feed shows delivery state only (awaiting / delivered / not delivered).
  Honest, smaller, but "verdict" never appears - the captain named it.
- (c) Extend `acknowledge_companion_events` with an optional `verdict` argument (default: today's
  behaviour). One call instead of two for the reader, but it couples the Companion outbox tool to
  this module's table and changes a shared tool's signature.

**D2 - what "calendar page" means.** `@lifekit-hq/ui` 0.2.0 has no calendar, date-picker or tabs
component, and new primitives must land in lifekit-common first (separate repo, publish, bump).
Options:

- (a) **Recommended.** An agenda: upcoming events grouped by day (Today / Tomorrow / date headers)
  over a selectable horizon (7 / 30 / 90 days) with kind filters, built from existing primitives
  (`cmn-chip`, `cmn-card`, `cmn-list-item-row`, `cmn-tag`, `cmn-empty-state`, `cmn-alert`). The
  fired feed is the second view on the same page (chip-tabs, the accounts-shell pattern).
- (b) A month grid. Needs a `cmn-calendar` component in lifekit-common first; this PR would wait on
  that publish.

Everything below is written against D1(a) and D2(a); D1(b) removes US3 and the write tool, D2(b)
changes US4's layout only.

---

## User Scenarios & Testing

### [US1] The calendar shows what is coming for my book (P1)

Denys (and the reader through MCP) sees every dated event that touches his holdings, watchlist,
theses and the macro backdrop, in one window, from the sources Finance Sentry already holds -
without a new table or feed.

**Independent Test**: with holdings `{MU}`, watchlist `{PLTR}`, one thesis on `MU` with a catalyst
dated inside the window, and the macro seed, `GET /api/v1/events/upcoming?from=&to=` returns
earnings / ex-dividend rows for MU and PLTR, the MU catalyst, the macro rows in the window, and a
filing-due row for MU derived from its last periodic filing; each row carries `kind`, `date`,
`subject`, `title`, `isEstimate`, `source`.

**Acceptance Scenarios**:

1. **Given** the window `[from, to]`, **When** upcoming events are requested, **Then** the result
   is the union of: earnings and ex-dividend dates for the user's equity holdings + watchlist
   (`kind = earnings | ex_dividend`), macro events in the window (`kind = macro`), catalysts of
   the user's unbroken theses dated in the window (`kind = thesis_catalyst`), and derived periodic
   filing due dates for the same tickers (`kind = filing_due`), sorted by date then subject.
2. **Given** one source throws or times out (Yahoo down, EDGAR 403), **When** upcoming events are
   requested, **Then** the other sources still return, and the response's `sources[]` marks that
   source `unavailable` - a day with nothing on it is never mistaken for a day nothing happens.
3. **Given** no `from`/`to`, **When** requested, **Then** the window is today .. today + 90 days;
   a window longer than 366 days or with `to < from` is rejected (400, `EVENTS_WINDOW_INVALID`).
4. **Given** `kinds=macro,earnings`, **When** requested, **Then** only those kinds are computed
   and returned (a filtered-out source is not called).
5. **Given** a broken thesis (`BrokenAt` set), **When** requested, **Then** its catalysts are not
   listed.

### [US2] A filing-due date is derived, flagged as an estimate, and never fabricated (P1)

No feed publishes SEC due dates. The module derives the next 10-Q/10-K due date from the last
periodic filing on EDGAR and the statutory deadline, and says it is an estimate.

**Independent Test**: `FilingDueCalculator` is pure; a table of (last 10-K report date, last
periodic filing form + report date, today) → (form, due date) or nothing, pinned by unit tests.

**Acceptance Scenarios**:

1. **Given** the latest periodic filing is a 10-Q for the period ending 2026-06-30 and the last
   10-K covered a period ending in December, **When** derived, **Then** the next period ends
   2026-09-30, the form is 10-Q, the due date is 2026-11-09 (40 days), `isEstimate = true`.
2. **Given** the latest periodic filing is a 10-Q for 2026-09-30 and the fiscal year ends in
   December, **When** derived, **Then** the next form is 10-K, due 2027-03-01 (60 days).
3. **Given** the derived due date is before today (delinquent filer, or EDGAR lag), **When**
   derived, **Then** nothing is emitted - the landed detector owns what actually filed.
4. **Given** a ticker with no EDGAR periodic filings (foreign issuer, crypto watchlist entry),
   **When** derived, **Then** nothing is emitted and no error is raised.
5. **Given** the deadline class is unknown, **When** derived, **Then** the large-accelerated-filer
   deadlines (40 / 60 days) apply and the row is marked an estimate - the module never claims a
   precision it does not have.

### [US3] The fired feed shows what happened and what the reader made of it (P1)

Every detector firing (earnings-ahead, filing-landed, news-cluster, intraday-move = market
structure, budget-breach) appears as a fired event with its delivery state and, when recorded,
the reader's verdict. Silence is a first-class outcome.

**Independent Test**: seed alerts of the five types plus companion rows in each disposition and
one verdict row; `GET /api/v1/events/fired` returns them newest first with the outcome table
below; `record_event_verdict` on a companion event id owned by the user makes the next read show
`outcome = verdict`.

**Outcome table** (pinned by tests):

| Companion disposition | Verdict row | `outcome` |
|---|---|---|
| any | present, `notified = true` | `verdict` |
| any | present, `notified = false` | `judged_immaterial` |
| `Delivered` | none | `silent` |
| `Pending`, `Dispatched`, `HeldForDigest`, `DeferredQuietHours` | none | `awaiting` |
| `SuppressedByMode`, `SuppressedByRateLimit`, `SuppressedByDedup`, `Failed` | none | `not_delivered` |
| no companion row yet | none | `awaiting` |

**Acceptance Scenarios**:

1. **Given** alerts of the five event types and of other types (SyncFailure, PriceHike), **When**
   the feed is read, **Then** only the five event types appear, newest first, paged
   (`page`, `pageSize` ≤ 100), dismissed alerts excluded.
2. **Given** a fired event whose companion row is `Delivered` with no verdict, **When** read,
   **Then** `outcome = silent` and `deliveredAt` is set - silence is shown, not hidden.
3. **Given** the reader calls `record_event_verdict(eventId, "Guidance cut is priced in; no
   action.", notified: false)`, **When** the feed is read, **Then** that event shows
   `outcome = judged_immaterial` with the text and `recordedAt`.
4. **Given** `record_event_verdict` is called with an event id that belongs to another user or
   does not exist, **When** handled, **Then** nothing is written and the tool returns
   `recorded = false`; a blank verdict is rejected the same way.
5. **Given** a verdict already exists for the event, **When** recorded again, **Then** it is
   replaced (the reader may revise), `recordedAt` updated.
6. **Given** the reader is absent (no OpenClaw, `AgentTriggerUrl` empty), **When** the feed is
   read, **Then** every row is `awaiting` or `not_delivered` and nothing errors - nothing here
   depends on the reader running.

### [US4] The Events page in the SPA (P1)

A lazy `/events` page with two views: **Calendar** (agenda grouped by day over 7 / 30 / 90 days,
kind chips) and **Fired** (the feed, outcome tag per row, verdict text inline, load more).

**Independent Test**: component + store specs pin: grouping by day with Today / Tomorrow labels,
the outcome label per `outcome` value, the source-unavailable notice, empty states per view, and
that switching horizon re-queries with the new window. e2e: the page renders both views from
mocked endpoints.

**Acceptance Scenarios**:

1. **Given** upcoming events across several days, **When** the calendar view renders, **Then**
   rows are grouped under day headers in date order, each row showing kind tag, subject, title,
   an "estimate" marker when `isEstimate`, and macro rows show their time when present.
2. **Given** `sources[]` marks `earnings` unavailable, **When** rendered, **Then** a `cmn-alert`
   states the calendar is missing that source; the rest of the agenda still renders.
3. **Given** the fired view, **When** rendered, **Then** each row shows severity, kind, subject,
   title, time ago, an outcome tag (`Verdict`, `Judged immaterial`, `Silent`, `Awaiting`,
   `Not delivered`) and the verdict text when present.
4. **Given** the horizon chip changes from 30 to 90 days, **When** clicked, **Then** the store
   re-queries with `to = today + 90` (pinned in the effects spec).
5. **Given** the sidebar, **When** rendered, **Then** "Events" appears as a nav item and a
   command-palette entry.

### [US5] The reader's MCP surface (P1)

One read tool and one write tool, named for the domain, not the consumer.

**Independent Test**: `get_event_calendar` returns `{upcoming, fired, sources}` for the
authenticated identity; `record_event_verdict` writes a verdict; the frozen tool-name contract
lists both; `ToolResolutionTests` constructs both from the shared graph.

**Acceptance Scenarios**:

1. **Given** an authenticated MCP identity, **When** `get_event_calendar(daysAhead = 14,
   daysBack = 7)` is called, **Then** it returns upcoming events in `[today, today + 14]`, fired
   events since `today - 7` (newest first, `limit` ≤ 100) with outcomes, and `sources[]`.
2. **Given** no resolvable identity, **When** either tool is called, **Then** the read returns
   null and the write returns `recorded = false`; no handler is invoked.
3. **Given** the tool surface, **When** the naming contract test runs, **Then** the agreed set is
   exactly the previous 60 names plus `get_event_calendar` and `record_event_verdict`; the write
   tool uses no `get_/list_/search_` prefix.

### Edge Cases

- A ticker held **and** watchlisted appears once per (kind, date).
- Yahoo's 6h in-process cache means the first calendar read after the cache expires pays one
  fetch per ticker (bounded by `MaxConcurrentFetches = 4`); the page shows a skeleton, the MCP call
  simply waits. If this proves too slow in practice the fallback is a materialised table refreshed
  by the existing earnings-ahead tick - not in this spec.
- Macro events are global (not user-scoped); the query returns them for every user.
- An alert with no companion row can be either "capture has not run yet" (≤ 1 minute) or "the
  type is unmapped in `MaterialityPolicy`". All five event types are mapped today; the module does
  not distinguish and reports `awaiting`.
- Companion rows purge at 90 days; a verdict outlives its companion row (365-day purge) and still
  renders on the alert while the alert row exists.
- The verdict text is bounded (≤ 2000 chars) and never logged.
- The fired feed is alert-centric: a Radar nightly market-structure alert and an intraday one
  both carry `MarketStructure` and both appear - the feed shows "what fired", not "which job".

## Requirements

### Functional Requirements

- **FR-001**: A new module `FinanceSentry.Modules.Events` (schema `events`) MUST own one table,
  `event_verdicts` (`Id`, `UserId`, `CompanionEventId` unique, `Verdict` ≤ 2000, `Notified`,
  `RecordedAt`), migrated by `MigrateAllModules`, with a retention decision registered.
- **FR-002**: Upcoming events MUST be computed at read time from the existing sources through
  read ports (`IUpcomingCorporateEventReader`, `IMacroEventReader`, `IThesisCatalystReader`,
  `IPeriodicFilingReader`) whose adapters live in `FinanceSentry.Integration`; the module MUST
  NOT reference Research, Alerts or Companion directly. No new external feed, no new job.
- **FR-003**: Filing due dates MUST be derived by a pure `FilingDueCalculator` from EDGAR periodic
  filings (10-K / 10-Q with `ReportDate`) as specified in US2, always `isEstimate = true`.
- **FR-004**: Fired events MUST be read through `IFiredAlertReader` (additive query in Alerts:
  alerts by type set, paged, non-dismissed) and enriched through `IEventDeliveryReader` (additive
  Companion lookup by `DedupKey` set and by id); no detector, alert type, silence window or
  generator code changes.
- **FR-005**: REST: `GET /api/v1/events/upcoming` (`from`, `to`, `kinds`) and
  `GET /api/v1/events/fired` (`page`, `pageSize`, `kinds`), user from the JWT, each with a
  contract test (401 without auth, response shape, 400 on an invalid window).
- **FR-006**: MCP: `get_event_calendar` (read) and `record_event_verdict` (write), thin adapters
  over the module's query/command handlers, identity from `IIdentityResolver`, optional `userId`
  override like the sibling tools; tool-name contract and `docs/mcp.md` updated.
- **FR-007**: SPA: lazy `modules/events` with the five-file `EventsStore` (page-scoped), an
  HTTP-only `EventsService`, models in `models/`, kind/outcome presentation registries in
  `constants/`, day-grouping in `utils/*.utils.ts` with a pipe for the template, `AppRoute.Events`,
  nav + palette entries. Unit specs for state / methods / computed / effects / utils and a
  component spec pinning behaviour; one e2e spec over mocked endpoints.
- **FR-008**: No surface (route, tool, table, field, job) MAY be named after Ledger; the reader is
  "the reader" or unnamed in code and docs (Principle VII).
- **FR-009**: `docs/claude/app-state.md` gains the Events block; this spec's status flips on merge.

### Key Entities

- **UpcomingEvent** (computed, not stored): `kind` (`earnings | ex_dividend | filing_due | macro |
  thesis_catalyst`), `date`, `time?`, `subject` (ticker, region or thesis ticker), `title`,
  `detail?`, `isEstimate`, `source` (URL or provider name), `referenceId?` (thesis / macro row id).
- **UpcomingEventsResult**: `items[]`, `from`, `to`, `sources[]` of `{source, status}` with
  `status ∈ ok | unavailable`.
- **FiredEvent** (read model): `alertId`, `kind` (alert type), `severity`, `subject`, `title`,
  `message`, `occurredAt`, `isRead`, `delivery` `{eventId?, disposition?, dispatchedAt?,
  deliveredAt?}`, `verdict?` `{text, notified, recordedAt}`, `outcome`.
- **EventVerdict** (stored): the reader's judgement on one companion event; one per event per
  user, replaceable.

## Success Criteria

### Measurable Outcomes

- **SC-001**: With the test user's book, `GET /events/upcoming` (90 days) returns rows of at least
  three kinds and `sources[]` all `ok` when Yahoo and EDGAR answer; with Yahoo blocked, the same
  call returns the other kinds and `earnings: unavailable` in under the request timeout.
- **SC-002**: Every fired event in the feed carries exactly one outcome from the table; the
  derivation is pinned by a table-driven unit test covering all nine dispositions × verdict
  presence.
- **SC-003**: Backend build 0 warnings; all suites green; the MCP name contract passes with 62
  names; two new REST contract tests pass.
- **SC-004**: Frontend lint, format check and unit tests pass in CI (they cannot run locally in
  this lane - registry credential missing); the e2e spec for `/events` passes in CI.

## Assumptions

- Upcoming events are computed live (no `upcoming_events` table, no refresh job) - see Edge
  Cases for the fallback. This keeps the module free of any infrastructure change (no cron, so no
  lifekit-dashboard catalog companion PR).
- The fired feed is limited to the five detector alert types named by the captain
  (`EarningsAhead`, `FilingLanded`, `NewsCluster`, `MarketStructure`, `BudgetBreach`). Widening to
  every companion-material kind is a one-line constant change later, not a design change.
- The filing-due derivation uses large-accelerated-filer deadlines for every filer; EDGAR's filer
  category is not parsed today and is out of scope.
- The thesis "trigger dates" of the captain's ask are `InvestmentThesis.Catalysts[].Date`;
  `InvalidationTriggers` are metric thresholds with no date and are not events.
- The reader's persona / hook-template change to call `record_event_verdict` is outside this repo
  (lifekit-stack platform patch) and follows this PR; until then the feed truthfully shows
  `silent` / `awaiting`.
- Frontend version is release-please-owned; no hand bump.
