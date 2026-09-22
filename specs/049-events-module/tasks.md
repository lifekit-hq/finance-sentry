# Tasks - Spec 049: Events Module

Gated on D1(a) and D2(a) in spec.md (routed to firstmate 2026-09-22). Tasks marked (D1) fall
away under D1(b); D2(b) changes T030's layout only.

## [US2] Filing due derivation (pure, first - no dependencies)

- [ ] T001 `FilingDueCalculator.Next(IReadOnlyList<PeriodicFiling>, DateOnly today)` in
      `Modules.Events/Domain` - next period end, form by fiscal-year-end month, 40 / 60 day
      deadline, null when past or empty
- [ ] T002 `FilingDueCalculatorTests` - table: 10-Q after 10-K, 10-K after Q3, past due → null,
      empty → null, month-end arithmetic (Feb / 30-day months), missing 10-K → 10-Q

## [US1] Upcoming events

- [ ] T003 Module skeleton: `FinanceSentry.Modules.Events.csproj`, `EventsModule` (registrar,
      DbContext, repositories, no jobs), `EventKind` constants, solution entries
- [ ] T004 Ports: `IUpcomingCorporateEventReader`, `IMacroEventReader`, `IThesisCatalystReader`,
      `IPeriodicFilingReader` (+ the ticker universe from Core's `IBrokerageHoldingsReader` /
      `IWatchlistReader`)
- [ ] T005 `GetUpcomingEventsQuery` handler - window validation (`EVENTS_WINDOW_INVALID`), kind
      filter, per-source isolation → `sources[]`, sort by date then subject, dedupe (kind, subject, date)
- [ ] T006 `GetUpcomingEventsQueryTests` - union of sources, one source throws → others returned +
      `unavailable`, kind filter skips the source call, broken thesis excluded, default window,
      invalid window rejected
- [ ] T007 Integration adapters: `EventsCorporateCalendarAdapter` (over
      `GetEarningsCalendarQuery`), `EventsMacroEventAdapter` (`IMacroCalendarService`),
      `EventsThesisCatalystAdapter` (`IThesisRepository`), `EventsPeriodicFilingAdapter`
      (`ISecEdgarService`, forms 10-K/10-Q) + `AddCrossModulePorts` registrations + adapter tests

## [US3] Fired feed and verdicts

- [ ] T008 Alerts additive: `IAlertRepository.GetByTypesPagedAsync` + `AlertRepository` impl +
      `GetAlertsByTypesQuery`; repository test
- [ ] T009 Companion additive: `ICompanionEventRepository.ListByDedupKeysAsync` + impl; test
- [ ] T010 Ports `IFiredAlertReader`, `IEventDeliveryReader`; Integration adapters
      `EventsFiredAlertAdapter`, `EventsDeliveryAdapter` (dedup key via `IMaterialityPolicy`)
- [ ] T011 `EventVerdict` entity, `EventsDbContext` (schema `events`, history table
      `__ef_migrations_history_events`), `EventsDbContextFactory`, migration `M001_InitialSchema`,
      `IEventVerdictRepository` + impl (upsert, list by event ids)
- [ ] T012 `EventOutcome.From(disposition?, verdict?)` + `EventOutcomeTests` (table over all
      dispositions × verdict presence × notified)
- [ ] T013 `GetFiredEventsQuery` handler - five kinds, paged, enrichment, outcome; tests
- [ ] T014 (D1) `RecordEventVerdictCommand` - foreign / unknown event → false, blank → false,
      upsert replaces; tests
- [ ] T015 `MigrateAllModules` + `RetentionPolicyRegistry` (`EventsDbContext`,
      `event_verdicts` purge 365 on `RecordedAt`)

## [US5] REST + MCP

- [ ] T016 `EventsController` - `GET events/upcoming`, `GET events/fired`; DTOs
- [ ] T017 `EventsContractTests` (`WebApplicationFactory`, ports mocked) - 401 without auth,
      shapes, 400 on invalid window, paging clamp
- [ ] T018 `GetEventCalendarTool` (`get_event_calendar`) + tests (no identity → null; window
      params → queries)
- [ ] T019 (D1) `RecordEventVerdictTool` (`record_event_verdict`) + tests (no identity → false;
      command receives the identity's user id)
- [ ] T020 `McpServiceRegistration.ModuleAssemblies` + csproj refs (API, MCP);
      `ToolNameContractTests` +2; `docs/mcp.md` rows

## [US4] SPA

- [ ] T021 `AppRoute.Events`, lazy route, nav item + palette entry
- [ ] T022 `models/event/event.model.ts` (`UpcomingEvent`, `UpcomingEventsResult`, `FiredEvent`,
      `FiredEventsPageResponse`, literal unions for kind / outcome / source status)
- [ ] T023 `constants/event/event.constants.ts` - `EVENT_KIND_META`, `OUTCOME_META`, horizons,
      page size; `error-messages.registry.ts` gains `EVENTS_WINDOW_INVALID`
- [ ] T024 `services/events.service.ts` (`ApiService`, prefix `events`) + spec
- [ ] T025 `utils/event-day.utils.ts` (`groupByDay`, `dayLabel`) + spec; `pipes/event-day-label.pipe.ts`
- [ ] T026 Store: `events.state.ts`, `events.methods.ts`, `events.computed.ts`, `events.effects.ts`
      (`loadUpcoming`, `loadFired`, `loadMoreFired`, hooks), `events.store.ts` (page-scoped)
- [ ] T027 Store specs: state, methods, computed (outcome label, grouped days, unavailable
      sources, empty), effects (horizon change re-queries with the new `to`; error code captured)
- [ ] T028 `pages/events/events.component.{ts,html}` - chip-tab row (Calendar / Fired), horizon
      + kind chips, agenda groups, fired rows with outcome tag + verdict, load more, empty states,
      skeletons, source-unavailable alert
- [ ] T029 `events.component.spec.ts` - grouping headers, outcome labels per value, unavailable
      notice, empty states, view switch
- [ ] T030 `frontend/e2e/events.spec.ts` over mocked `/api/v1/events/*`

## Docs

- [ ] T031 `docs/claude/app-state.md` Events block; spec status → Implemented

## Verification

- [ ] `dotnet build FinanceSentry.sln` 0 warnings (sdk:10.0 container)
- [ ] `dotnet test FinanceSentry.sln` green
- [ ] Frontend lint / format / unit / e2e: CI only in this lane (stated in the done line)
