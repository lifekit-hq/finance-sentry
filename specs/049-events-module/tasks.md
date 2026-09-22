# Tasks - Spec 049: Events Module

Gated on D1(a) and D2(a) in spec.md (routed to firstmate 2026-09-22). Tasks marked (D1) fall
away under D1(b); D2(b) changes T030's layout only.

## [US2] Filing due derivation (pure, first - no dependencies)

- [x] T001 `FilingDueCalculator.Next(IReadOnlyList<PeriodicFiling>, DateOnly today)` in
      `Modules.Events/Domain` - next period end, form by fiscal-year-end month, 40 / 60 day
      deadline, null when past or empty
- [x] T002 `FilingDueCalculatorTests` - table: 10-Q after 10-K, 10-K after Q3, past due → null,
      empty → null, month-end arithmetic (Feb / 30-day months), missing 10-K → 10-Q

## [US1] Upcoming events

- [x] T003 Module skeleton: `FinanceSentry.Modules.Events.csproj`, `EventsModule` (registrar,
      DbContext, repositories, no jobs), `EventKind` constants, solution entries
- [x] T004 Ports: `IUpcomingCorporateEventReader`, `IMacroEventReader`, `IThesisCatalystReader`,
      `IPeriodicFilingReader` (+ the ticker universe from Core's `IBrokerageHoldingsReader` /
      `IWatchlistReader`)
- [x] T005 `GetUpcomingEventsQuery` handler - window validation (`EVENTS_WINDOW_INVALID`), kind
      filter, per-source isolation → `sources[]`, sort by date then subject, dedupe (kind, subject, date)
- [x] T006 `GetUpcomingEventsQueryTests` - union of sources, one source throws → others returned +
      `unavailable`, kind filter skips the source call, broken thesis excluded, default window,
      invalid window rejected
- [x] T007 Integration adapters: `EventsCorporateCalendarAdapter` (over
      `GetEarningsCalendarQuery`), `EventsMacroEventAdapter` (`IMacroCalendarService`),
      `EventsThesisCatalystAdapter` (`IThesisRepository`), `EventsPeriodicFilingAdapter`
      (`ISecEdgarService`, forms 10-K/10-Q) + `AddCrossModulePorts` registrations + adapter tests

## [US3] Fired feed and verdicts

- [x] T008 Alerts additive: `IAlertRepository.GetByTypesPagedAsync` + `AlertRepository` impl +
      `GetAlertsByTypesQuery`; repository test
- [x] T009 Companion additive: `ICompanionEventRepository.ListByDedupKeysAsync` + impl; test
- [x] T010 Ports `IFiredAlertReader`, `IEventDeliveryReader`; Integration adapters
      `EventsFiredAlertAdapter`, `EventsDeliveryAdapter` (dedup key via `IMaterialityPolicy`)
- [x] T011 `EventVerdict` entity, `EventsDbContext` (schema `events`, history table
      `__ef_migrations_history_events`), `EventsDbContextFactory`, migration `M001_InitialSchema`,
      `IEventVerdictRepository` + impl (upsert, list by event ids)
- [x] T012 `EventOutcome.From(disposition?, verdict?)` + `EventOutcomeTests` (table over all
      dispositions × verdict presence × notified)
- [x] T013 `GetFiredEventsQuery` handler - five kinds, paged, enrichment, outcome; tests
- [x] T014 (D1) `RecordEventVerdictCommand` - foreign / unknown event → false, blank → false,
      upsert replaces; tests
- [x] T015 `MigrateAllModules` + `RetentionPolicyRegistry` (`EventsDbContext`,
      `event_verdicts` purge 365 on `RecordedAt`)

## [US5] REST + MCP

- [x] T016 `EventsController` - `GET events/upcoming`, `GET events/fired`; DTOs
- [x] T017 `EventsContractTests` (`WebApplicationFactory`, ports mocked) - 401 without auth,
      shapes, 400 on invalid window, paging clamp
- [x] T018 `GetEventCalendarTool` (`get_event_calendar`) + tests (no identity → null; window
      params → queries)
- [x] T019 (D1) `RecordEventVerdictTool` (`record_event_verdict`) + tests (no identity → false;
      command receives the identity's user id)
- [x] T020 `McpServiceRegistration.ModuleAssemblies` + csproj refs (API, MCP);
      `ToolNameContractTests` +2; `docs/mcp.md` rows

## [US4] SPA

- [x] T021 `AppRoute.Events`, lazy route, nav item + palette entry
- [x] T022 `models/event/event.model.ts` (`UpcomingEvent`, `UpcomingEventsResult`, `FiredEvent`,
      `FiredEventsPageResponse`, literal unions for kind / outcome / source status)
- [x] T023 `constants/event/event.constants.ts` - `EVENT_KIND_META`, `OUTCOME_META`, horizons,
      page size; `error-messages.registry.ts` gains `EVENTS_WINDOW_INVALID`
- [x] T024 `services/events.service.ts` (`ApiService`, prefix `events`) + spec
- [x] T025 `utils/event-day.utils.ts` (`groupByDay`, `dayLabel`) + spec; `pipes/event-day-label.pipe.ts`
- [x] T026 Store: `events.state.ts`, `events.methods.ts`, `events.computed.ts`, `events.effects.ts`
      (`loadUpcoming`, `loadFired`, `loadMoreFired`, hooks), `events.store.ts` (page-scoped)
- [x] T027 Store specs: state, methods, computed (outcome label, grouped days, unavailable
      sources, empty), effects (horizon change re-queries with the new `to`; error code captured)
- [x] T028 `pages/events/events.component.{ts,html}` - chip-tab row (Calendar / Fired), horizon
      + kind chips, agenda groups, fired rows with outcome tag + verdict, load more, empty states,
      skeletons, source-unavailable alert
- [x] T029 `events.component.spec.ts` - grouping headers, outcome labels per value, unavailable
      notice, empty states, view switch
- [x] T030 `frontend/e2e/events.spec.ts` over mocked `/api/v1/events/*`

## Docs

- [x] T031 `docs/claude/app-state.md` Events block; spec status → Implemented

## Verification

- [x] `dotnet build FinanceSentry.sln` 0 warnings (sdk:10.0 container)
- [x] `dotnet test FinanceSentry.sln` green
- [x] Frontend lint + format check + events unit specs run locally against a sibling worktree's install (`@lifekit-hq/ui` 0.3.1 there vs 0.2.0 in this lockfile); e2e spec runs in CI only
