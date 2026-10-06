---
description: "Task list for Companion Notification Modes + Event-Driven Push"
---

# Tasks: Companion Notification Modes + Event-Driven Push

**Input**: Design documents from `/specs/031-companion-notifications/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/mcp-tools.md

**Tests**: Constitution mandates unit tests for business logic (materiality, dedup, mode→disposition, digest consolidation, watermark, mode set/validate). No new external source and no REST endpoints → no external-contract or REST tests. The outbound webhook is our own contract → a payload serialization unit test.

**Organization**: By user story — US1 mode control (P1, MVP), US2 capture + dispatch (P2), US3 digest (P3).

## Path Conventions
- New module: `backend/src/FinanceSentry.Modules.Companion/`
- Cross-module read contracts: `backend/src/FinanceSentry.Core/Interfaces/`
- MCP tools: `backend/src/FinanceSentry.Mcp/Tools/`
- Tests: `backend/tests/FinanceSentry.Modules.Companion.Tests/`

---

## Phase 1: Setup

- [X] T001 Create `backend/src/FinanceSentry.Modules.Companion/FinanceSentry.Modules.Companion.csproj` (net9.0, refs to `FinanceSentry.Core`; mirror `FinanceSentry.Modules.Risk.csproj`) and add it to `backend/FinanceSentry.sln`
- [X] T002 Create test project `backend/tests/FinanceSentry.Modules.Companion.Tests/FinanceSentry.Modules.Companion.Tests.csproj` (xUnit + FluentAssertions + `Microsoft.EntityFrameworkCore.InMemory`; mirror the Research test csproj) and add to the solution
- [X] T003 Register the module in `backend/src/FinanceSentry.API/Program.cs` (CQRS assembly scan for the Companion assembly + `AddCompanionModule`), mirroring how Risk/Research are registered

---

## Phase 2: Foundational (blocks all stories)

**⚠️ Migration MUST ship with its `.Designer.cs` (M007/M008 lesson).**

### Domain
- [X] T004 [P] Create `NotificationMode` enum (`Quiet|Digest|Scan|Realtime`) in `Domain/NotificationMode.cs`
- [X] T005 [P] Create `CompanionEventKind` (`RiskViolation|SyncFailure|UnusualSpend|Opportunity|ThesisBreak|AnalystAction`) and `EventDisposition` (`Pending|Dispatched|HeldForDigest|Delivered|SuppressedByMode|SuppressedByDedup|SuppressedByRateLimit|DeferredQuietHours|Failed`) in `Domain/CompanionEventKind.cs` and `Domain/EventDisposition.cs`
- [X] T006 [P] Create `CompanionNotificationSetting` entity in `Domain/CompanionNotificationSetting.cs` (per data-model)
- [X] T007 [P] Create `CompanionEvent` entity in `Domain/CompanionEvent.cs` (per data-model)
- [X] T008 [P] Create `CompanionCaptureState` entity (`Source` PK, `Watermark`) in `Domain/CompanionCaptureState.cs`

### Repositories
- [X] T009 [P] Create `INotificationSettingRepository` (`GetOrDefaultAsync(userId)`, `UpsertAsync`) in `Domain/Repositories/` + impl in `Infrastructure/Persistence/Repositories/NotificationSettingRepository.cs`
- [X] T010 [P] Create `ICompanionEventRepository` (`InsertIfNewAsync` dedup-by-key, `ListByDispositionAsync(userId, dispositions, limit)`, `MarkAsync(ids, disposition, fields)`, `ListRealtimePendingAsync`) in `Domain/Repositories/` + impl in `Infrastructure/Persistence/Repositories/CompanionEventRepository.cs`
- [X] T011 [P] Create `ICompanionCaptureStateRepository` (`GetWatermarkAsync(source)`, `SetWatermarkAsync`) + impl

### DbContext + migration (sequential — same files)
- [X] T012 Create `Infrastructure/Persistence/CompanionDbContext.cs` (schema `companion`; DbSets; unique index on `CompanionNotificationSetting.UserId`; unique `CompanionEvent.DedupKey`; indexes `(UserId,Disposition,OccurredAt)`; enum-as-string conversions) and `CompanionDbContextFactory.cs` (mirror `RiskDbContextFactory`, history table `__ef_migrations_history_companion`)
- [X] T013 Generate migration **M001_InitialSchema** WITH its `.Designer.cs` via `dotnet ef migrations add M001_InitialSchema` (creates `companion_notification_settings`, `companion_events`, `companion_capture_state`); verify snapshot + Designer present, and the M001 chain applies clean against a throwaway Postgres
- [X] T014 Create `CompanionModule.cs` — `AddCompanionModule` (DbContext with `MigrationsHistoryTable`, repositories) + `IJobRegistrar` stub (jobs registered in later phases); wire DI

**Checkpoint**: `dotnet build backend/` zero warnings; migration discoverable.

---

## Phase 3: User Story 1 — Mode control (P1) 🎯 MVP

**Goal**: Per-user notification mode, readable and settable via MCP, persisted, effective immediately. Fully pull-based — no capture needed.

**Independent Test**: `get_notification_mode` → default `scan`; `set_notification_mode {"mode":"quiet"}` persists; invalid mode rejected; change needs no redeploy.

- [X] T015 [P] [US1] Unit test for `SetNotificationModeCommand` (valid set persists; invalid mode rejected, previous unchanged) in `.../SetNotificationModeTests.cs`
- [X] T016 [P] [US1] Create `GetNotificationModeQuery` + handler (returns effective settings, defaults if no row) in `Application/Queries/GetNotificationModeQuery.cs` + `NotificationModeDto` in `API/Responses/`
- [X] T017 [US1] Create `SetNotificationModeCommand` + handler (parse/validate mode, upsert) in `Application/Commands/SetNotificationModeCommand.cs`
- [X] T018 [P] [US1] Implement `get_notification_mode` MCP tool in `backend/src/FinanceSentry.Mcp/Tools/GetNotificationModeTool.cs`
- [X] T019 [P] [US1] Implement `set_notification_mode` MCP tool in `backend/src/FinanceSentry.Mcp/Tools/SetNotificationModeTool.cs`
- [X] T020 [US1] Update `ToolNameContractTests` agreed surface in `backend/tests/FinanceSentry.Mcp.Tests/ContractTests/ToolNameContractTests.cs` (add the 2 US1 tools)

**Checkpoint**: US1 shippable — mode get/set works end-to-end via MCP.

---

## Phase 4: User Story 2 — Capture + realtime dispatch (P2)

**Goal**: Poll detectors for material events, dedup, write with mode-based disposition; realtime → outbound webhook wake; agent pulls + acks.

**Independent Test**: realtime → material event captured + dispatched ≤60s exactly once; quiet → recorded `SuppressedByMode`, no push; duplicate → one row; unreachable URL → retried not lost.

### Cross-module read contracts
- [X] T021 [P] [US2] Create `IMaterialAlertReader` in `backend/src/FinanceSentry.Core/Interfaces/IMaterialAlertReader.cs` + impl in the Alerts module (`GetNewSinceAsync(watermark)`)
- [X] T022 [P] [US2] Create `IThesisBreakReader` + `IAnalystActionFeedReader` in `Core/Interfaces/` + impls in the Research module (new since watermark)

### Materiality + capture
- [X] T023 [P] [US2] Unit test for `MaterialityPolicy` (which kinds are material; analyst action only on held ticker; dedup-key construction) in `.../MaterialityPolicyTests.cs`
- [X] T024 [P] [US2] Unit test for mode→disposition mapping (quiet→SuppressedByMode, digest→HeldForDigest, scan/realtime→Pending) in `.../DispositionTests.cs`
- [X] T025 [US2] Create `IMaterialityPolicy` + `MaterialityPolicy` (classify + dedup key) in `Application/Services/`
- [X] T026 [US2] Create `ICompanionEventCapture` + `CompanionEventCapture` (read sources via watermark, filter held tickers via `IBrokerageHoldingsReader`, apply materiality + dedup, compute disposition from current mode, insert-if-new, advance watermark) in `Application/Services/`
- [X] T027 [US2] Create `CompanionOptions` (AgentTriggerUrl, quiet-hours defaults, MaxProactivePerHour, DigestHourLocal, TimeZoneId) in `Application/Services/CompanionOptions.cs`; bind in `AddCompanionModule`

### Dispatch
- [X] T028 [P] [US2] Unit test for the outbound wake payload (ids/refs only, no secrets/detail) in `.../WebhookPayloadTests.cs`
- [X] T029 [US2] Create `IAgentWakeDispatcher` + `WebhookAgentWakeDispatcher` (POST minimal payload to `AgentTriggerUrl`; no-op when unset; return success/failure) in `Application/Services/` + named HttpClient in `AddCompanionModule`
- [X] T030 [US2] Create `CompanionCaptureJob` (`[DisableConcurrentExecution]`) and `CompanionDispatchJob` (realtime relay: honor quiet-hours + rate-limit → `DeferredQuietHours`/`SuppressedByRateLimit`; POST → `Dispatched`/`Failed` with `Attempts`/`LastError`) in `Infrastructure/Jobs/`

### Query surface
- [X] T031 [P] [US2] Create `GetPendingCompanionEventsQuery` + handler (pending/dispatched, optional held-for-digest) in `Application/Queries/` + `CompanionEventDto` in `API/Responses/`
- [X] T032 [US2] Create `AcknowledgeCompanionEventsCommand` + handler (mark `Delivered`) in `Application/Commands/`
- [X] T033 [P] [US2] Implement `get_pending_companion_events` MCP tool in `backend/src/FinanceSentry.Mcp/Tools/GetPendingCompanionEventsTool.cs`
- [X] T034 [P] [US2] Implement `acknowledge_companion_events` MCP tool in `backend/src/FinanceSentry.Mcp/Tools/AcknowledgeCompanionEventsTool.cs`
- [X] T035 [US2] Register `companion-capture` (every 1 min) + `companion-dispatch` (every 1 min) recurring jobs in `CompanionModule.cs`; add the 2 tools to `ToolNameContractTests`

**Checkpoint**: US2 works — capture + realtime dispatch + pull/ack.

---

## Phase 5: User Story 3 — Daily digest (P3)

**Goal**: In `digest` mode, hold events and consolidate once/day.

**Independent Test**: digest mode → events `HeldForDigest`, none dispatched immediately; digest run surfaces the day's set once; empty day → no forced message.

- [X] T036 [P] [US3] Unit test for digest consolidation (collects `HeldForDigest` for the user, one batch, no repeat after delivery/ack; empty → nothing) in `.../DigestConsolidationTests.cs`
- [X] T037 [US3] Create `CompanionDigestJob` (`[DisableConcurrentExecution]`, daily at `DigestHourLocal`): for `digest`-mode users, expose the day's `HeldForDigest` events for the agent to pull via `get_pending_companion_events {includeHeldForDigest:true, heldOverrideReason:"daily digest"}` (the Digest wake payload carries both); mark surfaced set so it isn't repeated in `Infrastructure/Jobs/CompanionDigestJob.cs`
- [X] T038 [US3] Register `companion-digest` recurring job (daily) in `CompanionModule.cs`

**Checkpoint**: All three stories functional.

---

## Phase 6: Polish & Cross-Cutting

- [X] T039 `/csharp-quality` sweep across all new files; `dotnet build backend/` zero warnings
- [X] T040 Add `Companion` config section to `backend/src/FinanceSentry.API/appsettings.json` (defaults; `AgentTriggerUrl` empty = pull-only) and document it
- [X] T041 Bump backend `<Version>` in `backend/src/FinanceSentry.API/FinanceSentry.API.csproj`
- [X] T042 Run `quickstart.md` verification (US1/US2/US3 + boundary) against the Docker stack; confirm M001 in `__ef_migrations_history_companion`

---

## Dependencies & Execution Order

- **Setup (P1)** → **Foundational (P2)** blocks everything. T012→T013→T014 sequential (shared files); T004–T011 parallel.
- **US1 (P3)** depends only on Foundational — the MVP; fully pull-based, no capture.
- **US2 (P4)** depends on Foundational; adds read contracts + capture + dispatch. Independent of US1.
- **US3 (P5)** depends on Foundational + the event outbox from US2 (needs `HeldForDigest` events to exist).
- **Polish (P6)** after desired stories.

## Implementation Strategy

1. Setup + Foundational (migration discoverable, zero warnings).
2. **US1 → STOP & VALIDATE**: get/set mode via MCP. Ship — this alone gives the "control the dial" value with zero outbound integration.
3. US2 → validate capture + realtime/pull.
4. US3 → validate digest.
5. Polish.

## Notes
- Constitution gates per file: `dotnet build backend/` zero warnings; unit tests for business logic.
- M001 MUST carry its `.Designer.cs` (M007/M008 lesson) — verify via the quickstart migration-history check.
- No new FS push channel on the agent path (FR-015, amended by spec 859 for Web Push): the only outbound is the configurable webhook carrying ids/refs.
- Cross-module reads go through `Core.Interfaces` contracts (constitution I) — no concrete cross-module references.
