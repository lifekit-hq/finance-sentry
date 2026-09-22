# Implementation Plan: Events Module

**Branch**: `fm/fs-events-module` | **Date**: 2026-09-22 | **Spec**: [spec.md](./spec.md)

## Architecture Decisions

- **A new module, not a Research feature.** Events spans Research (calendars, theses, EDGAR),
  Alerts (what fired) and Companion (whether it reached the reader). Putting it in any one of
  them would make that module reference the other two. `FinanceSentry.Modules.Events` references
  Core + Infrastructure only, declares its read ports in `Domain/Ports/`, and the adapters live in
  `FinanceSentry.Integration` exactly like 039's `IpsAllocationPolicySource` and 421's
  `AssetSignalAdapter` - the composition library every host (API, MCP) already registers through
  `AddCrossModulePorts()`. Integration gains project references to Alerts, Companion and Events.

- **Upcoming events are computed at read time.** Three of the four sources are already live reads
  (`GetEarningsCalendarQuery` resolves holdings + watchlist and calls Yahoo behind a 6h cache;
  `IMacroCalendarService.QueryAsync`; `IThesisRepository.ListAsync` with an in-memory catalyst
  filter). Materialising them would add a table, a job (and therefore a lifekit-dashboard
  catalog entry), a retention row and a staleness story, to save a page load a few seconds once
  every six hours. The fan-out isolates each source: a throwing source contributes nothing and a
  `sources[]` entry reads `unavailable`. The fallback (materialise off the earnings-ahead tick)
  is recorded in the spec's edge cases, not built.

- **Filing due dates are a pure calculator over EDGAR periodic filings.** `FilingDueCalculator
  .Next(filings, today)` takes the ticker's recent 10-K/10-Q rows (`Form`, `ReportDate`) and
  returns `(form, periodEnd, dueDate)` or null. Next period end = latest periodic `ReportDate`
  + 3 months; it is a 10-K when that month equals the last 10-K's period-end month, else a 10-Q;
  deadline 60 / 40 days (large accelerated filer - the only class Finance Sentry can assume
  without parsing the filer category); a due date already in the past yields nothing. Pure,
  no clock injection needed beyond the `today` argument, tested by table.

- **The fired feed is alert-centric, enriched left-to-right.** Page the user's alerts of the five
  event types (new Alerts query `GetAlertsByTypesQuery` → `IAlertRepository.GetByTypesPagedAsync`,
  additive, same shape as `GetPagedAsync`), then one batch lookup of companion rows by
  `DedupKey ∈ {alert:{id}}` (new `ICompanionEventRepository.ListByDedupKeysAsync`, additive), then
  one batch lookup of verdicts by alert id (this module's table). Outcome derivation is
  a pure function `EventOutcome.From(disposition?, verdict?)` pinned by a table test. Nothing in
  `AlertGeneratorService`, `MaterialityPolicy`, the detectors or the dispatch loop changes.

- **The verdict is keyed by the companion event id** because that is the id the reader holds
  (wake payload, `get_pending_companion_events`); the alert id never reaches it. `RecordEvent
  VerdictCommand` validates the event through `IEventDeliveryReader.FindAsync(userId, eventId)`
  (adapter over `ICompanionEventRepository.GetAsync` + user check) so a foreign or unknown id
  writes nothing. The adapter reads the alert id back from the row's dedup key
  (`IMaterialityPolicy.AlertIdFromDedupKey`); an event not captured from an alert writes nothing.
  The alert id is persisted on the verdict and is the feed's lookup key, so the verdict survives
  the companion row's 90-day purge. Upsert on `(UserId, CompanionEventId)`.

- **MCP tools are thin adapters, as every sibling.** `GetEventCalendarTool` composes the two
  query handlers (`GetUpcomingEventsQuery`, `GetFiredEventsQuery`) into one response so the
  reader makes one call; `RecordEventVerdictTool` wraps the command. Events joins
  `McpServiceRegistration.ModuleAssemblies`; `ToolNameContractTests` grows by two;
  `AgentModule.RegisterMcpToolSurface` picks both up for the browser agent automatically.

- **SPA: one page, two views, one page-scoped store.** `EventsStore` holds `view`
  (`calendar | fired`), `horizonDays` (7 / 30 / 90), `kinds`, the upcoming result, the fired page
  (`hasMore` / `loadMore` like `transaction-ledger`), statuses and error codes. Effects:
  `loadUpcoming` (switchMap on horizon/kinds), `loadFired` / `loadMoreFired`. Day grouping is
  `EventDayUtils.groupByDay(items, today)` in `utils/event-day.utils.ts` with an `eventDayLabel`
  pipe for the template. Presentation registries (`EVENT_KIND_META`, `OUTCOME_META`) follow
  `ALERT_TYPE_META_REGISTRY` with a default entry so a backend-added kind never breaks the page.
  The chip-tab row reuses the accounts-shell `role="tablist"` pattern; no new `cmn-*` component.

## Cross-module touch points (all additive)

| Module | Change | Why it is not "shared code" |
|---|---|---|
| Alerts | `IAlertRepository.GetByTypesPagedAsync` + `AlertRepository` impl; `GetAlertsByTypesQuery` handler | new read method, no existing caller changes |
| Companion | `ICompanionEventRepository.ListByDedupKeysAsync` + impl | new read method |
| Integration | five adapters + `AddCrossModulePorts` registrations; csproj refs | the designated home for cross-module ports |
| API | csproj ref to Events; `MigrateAllModules` gains `EventsDbContext` | host composition |
| MCP | csproj ref; `ModuleAssemblies` entry; two tools | host composition |
| Retention | `EventsDbContext` in the known-contexts list; `Purge(Events, "event_verdicts", "RecordedAt", 365)` | the registry is the coverage guard |

## Files

| Area | Path | Change |
|---|---|---|
| Events module | `backend/src/FinanceSentry.Modules.Events/{EventsModule.cs, FinanceSentry.Modules.Events.csproj}` | new |
| | `Domain/{EventVerdict.cs, EventKind.cs, EventOutcome.cs, FilingDueCalculator.cs, Repositories/IEventVerdictRepository.cs}` | new |
| | `Domain/Ports/{IUpcomingCorporateEventReader, IMacroEventReader, IThesisCatalystReader, IPeriodicFilingReader, IFiredAlertReader, IEventDeliveryReader}.cs` | new |
| | `Application/Queries/{GetUpcomingEventsQuery, GetFiredEventsQuery}.cs`, `Application/Commands/RecordEventVerdictCommand.cs` | new |
| | `API/Controllers/EventsController.cs`, `API/Responses/{UpcomingEventDto, UpcomingEventsResult, FiredEventDto, FiredEventsPageResponse, EventSourceStatusDto}.cs` | new |
| | `Infrastructure/Persistence/{EventsDbContext, EventsDbContextFactory, Repositories/EventVerdictRepository}.cs`, `Migrations/…_M001_InitialSchema.cs` (+ Designer, snapshot) | new |
| Alerts | `Domain/Repositories/IAlertRepository.cs`, `Infrastructure/Persistence/Repositories/AlertRepository.cs`, `Application/Queries/GetAlertsByTypesQuery.cs` | additive |
| Companion | `Domain/Repositories/ICompanionEventRepository.cs`, `Infrastructure/Persistence/Repositories/CompanionEventRepository.cs` | additive |
| Integration | `Events{CorporateCalendar, MacroEvent, ThesisCatalyst, PeriodicFiling, FiredAlert, EventDelivery}Adapter.cs`, `CrossModulePortRegistration.cs`, csproj | new + edit |
| API / MCP hosts | `FinanceSentry.API.csproj`, `Migrations/MigrationExtensions.cs`, `FinanceSentry.Mcp.csproj`, `McpServiceRegistration.cs`, `Tools/{GetEventCalendarTool, RecordEventVerdictTool}.cs` | edit + new |
| Retention | `Application/RetentionPolicyRegistry.cs` | edit |
| Solution | `backend/FinanceSentry.sln` | two projects |
| Tests | `backend/tests/FinanceSentry.Modules.Events.Tests/{FilingDueCalculatorTests, EventOutcomeTests, GetUpcomingEventsQueryTests, GetFiredEventsQueryTests, RecordEventVerdictCommandTests}.cs` | new project |
| | `backend/tests/FinanceSentry.Tests.Integration/Events/EventsContractTests.cs` | new |
| | `backend/tests/FinanceSentry.Tests.Integration/CrossModulePorts/Events*AdapterTests.cs` | new |
| | `backend/tests/FinanceSentry.Mcp.Tests/{GetEventCalendarToolTests, RecordEventVerdictToolTests}.cs`, `ContractTests/ToolNameContractTests.cs` | new + edit |
| | Alerts / Companion repository method tests beside the existing repository suites | new |
| Frontend | `frontend/src/app/modules/events/{models/event/event.model.ts, constants/event/event.constants.ts, services/events.service.ts, store/events.{state,computed,methods,effects,store}.ts, utils/event-day.utils.ts, pipes/event-day-label.pipe.ts, pages/events/events.component.{ts,html}}` + specs | new |
| | `shared/enums/app-route/app-route.enum.ts`, `app.routes.ts`, `core/shell/app-shell.component.ts`, `core/errors/error-messages.registry.ts` | edit |
| | `frontend/e2e/events.spec.ts` | new |
| Docs | `docs/mcp.md`, `docs/claude/app-state.md`, this spec folder | edit |

## Verification plan

- Backend: `dotnet build` (0 warnings) and `dotnet test` in `mcr.microsoft.com/dotnet/sdk:10.0`
  (no local SDK in this lane), integration suites under Testcontainers where they already are.
- Frontend: cannot lint/test locally (no registry credential for `@lifekit-hq/*`); every `.ts`
  is written to the ESLint rules in `docs/claude/frontend-rules.md` and the real gate runs in CI.
  The done line states this explicitly.
