# Implementation Plan: Companion Notification Modes + Event-Driven Push

**Branch**: `031-companion-notifications` | **Date**: 2026-07-22 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/031-companion-notifications/spec.md`

## Summary

Finance Sentry gains a **new `Companion` module** that owns the notification *policy*: a per-user notification **mode** (`quiet | digest | scan | realtime`, default `scan`) and a **material-event outbox** (`companion_events`). A frequent Hangfire **capture job** polls the existing detectors (alerts, thesis breaks, analyst actions on held names) through thin read contracts, applies a materiality + dedup policy, and writes events with a disposition derived from the current mode. A **dispatch relay** turns realtime events into an outbound webhook "wake" to the agent runtime (configurable URL; no-op + pull-fallback when unset), and a **digest job** consolidates the day for `digest` mode. The agent reads/flips the mode and pulls pending events over MCP; **delivery stays in the agent runtime**. On the agent path FS adds no user-facing push channel (Web Push is a separate channel, see [spec 859](../859-web-push-notifications/spec.md)).

This is a *focused outbox* — a deliberate stepping-stone toward the full `026-event-bus-outbox`, not a dependency on it.

## Technical Context

**Language/Version**: C# 13 / .NET 9
**Primary Dependencies**: ASP.NET Core 9, EF Core 9 (Npgsql), Hangfire, `FinanceSentry.Core.Cqrs` (hand-rolled `ICommand`/`IQuery` — **no MediatR**), `ModelContextProtocol` SDK, `System.Net.Http` (outbound webhook — no new NuGet packages)
**Storage**: PostgreSQL 14 — **new `CompanionDbContext`** (schema `companion`, history table `__ef_migrations_history_companion`), migration `M001_InitialSchema` creating `companion_notification_settings` and `companion_events`. No changes to existing module schemas.
**Testing**: xUnit + FluentAssertions; EF InMemory for handler/service tests. Unit tests for materiality, dedup, mode→disposition, digest consolidation, watermark. No REST endpoints → no REST contract tests; MCP tools are thin over CQRS handlers.
**Target Platform**: Linux server (Docker), single-node; deployed by the CI self-hosted runner.
**Project Type**: Backend modular monolith + MCP (backend only — **no frontend changes**).
**Performance Goals**: realtime dispatch within 60s of detection (SC-002) → capture poll cadence ≤ 60s for realtime; digest once/day.
**Constraints**: no secrets in dispatch payload; no user data crossing users; zero-warning build; migration ships with its `.Designer.cs`.
**Scale/Scope**: single primary user; low event volume (a handful/day typical).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Modular isolation / contracts (no cross-module concrete coupling)**: PASS — the Companion module reads other modules ONLY through thin read contracts placed in `FinanceSentry.Core.Interfaces` (same pattern as `IBrokerageHoldingsReader`), implemented by the source modules (Alerts, Research). No concrete cross-module references.
- **II. CQRS hand-rolled (no MediatR)**: PASS — `ICommand`/`IQuery` handlers, auto-registered by assembly scan.
- **III. Multi-source isolation / per-user**: PASS — settings + events are per-`UserId`; queries scoped by user.
- **IV. One concept per file**: PASS — entities, enums, repos, services each in their own file.
- **Testing discipline**: PASS — unit tests for all business logic (materiality, dedup, disposition, digest, watermark); no new external source → the "external-contract test" gate does not apply (the outbound webhook is our own contract, covered by a serialization/unit test).
- **Migration discipline (M007/M008 lesson)**: PASS — `M001_InitialSchema` generated WITH its `.Designer.cs` via `dotnet ef`, verified discoverable; `CompanionDbContextFactory` for design-time.
- **Zero-warning build**: PASS — `dotnet build backend/` clean after every `.cs`.

No violations → Complexity Tracking not required.

## Project Structure

### Documentation (this feature)

```text
specs/031-companion-notifications/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── mcp-tools.md      # Phase 1 output — MCP tool contracts
└── tasks.md             # Phase 2 output (/speckit.tasks — NOT created here)
```

### Source Code (repository root)

```text
backend/src/FinanceSentry.Modules.Companion/           # NEW module
├── CompanionModule.cs                                 # DI + Hangfire job registration
├── Domain/
│   ├── NotificationMode.cs                            # enum: Quiet|Digest|Scan|Realtime
│   ├── CompanionNotificationSetting.cs                # per-user preference
│   ├── CompanionEvent.cs                              # the outbox row
│   ├── CompanionEventKind.cs                          # enum: RiskViolation|SyncFailure|UnusualSpend|Opportunity|ThesisBreak|AnalystAction
│   ├── EventDisposition.cs                            # enum: Pending|Dispatched|HeldForDigest|Suppressed*|Deferred*
│   └── Repositories/{INotificationSettingRepository,ICompanionEventRepository}.cs
├── Application/
│   ├── Queries/{GetNotificationModeQuery,GetPendingCompanionEventsQuery}.cs
│   ├── Commands/{SetNotificationModeCommand,AcknowledgeCompanionEventsCommand}.cs
│   └── Services/
│       ├── IMaterialityPolicy.cs + MaterialityPolicy.cs      # what counts, dedup key
│       ├── ICompanionEventCapture.cs + CompanionEventCapture.cs
│       ├── IAgentWakeDispatcher.cs + WebhookAgentWakeDispatcher.cs
│       └── CompanionOptions.cs                                # AgentTriggerUrl, quiet-hours, rate-limit
├── Infrastructure/
│   ├── Persistence/{CompanionDbContext,CompanionDbContextFactory}.cs
│   ├── Persistence/Repositories/{NotificationSettingRepository,CompanionEventRepository}.cs
│   └── Jobs/{CompanionCaptureJob,CompanionDispatchJob,CompanionDigestJob}.cs
└── Migrations/20260722_M001_InitialSchema{,.Designer}.cs + CompanionDbContextModelSnapshot.cs

backend/src/FinanceSentry.Core/Interfaces/             # NEW read contracts (cross-module boundary)
├── IMaterialAlertReader.cs                            # implemented by Alerts module
├── IThesisBreakReader.cs                              # implemented by Research module
└── IAnalystActionFeedReader.cs                        # implemented by Research module
  # (IBrokerageHoldingsReader already exists — reused to resolve "held names")

backend/src/FinanceSentry.Mcp/Tools/                   # NEW MCP tools (thin over handlers)
├── GetNotificationModeTool.cs
├── SetNotificationModeTool.cs
├── GetPendingCompanionEventsTool.cs
└── AcknowledgeCompanionEventsTool.cs

backend/tests/FinanceSentry.Modules.Companion.Tests/   # NEW test project
└── {MaterialityPolicyTests,CompanionEventCaptureTests,DispositionTests,DigestConsolidationTests,SetNotificationModeTests,WebhookPayloadTests}.cs
```

**Structure Decision**: A dedicated `FinanceSentry.Modules.Companion` module (mirrors the `Risk` module layout: own `DbContext` + design-time factory + `Module` registration class + `Migrations/`). Keeps notification policy cohesive and out of Research/Alerts. Cross-module reads go through new contracts in `Core.Interfaces`, implemented by the source modules — the constitutional boundary, not direct coupling.

## Complexity Tracking

No constitution violations — section intentionally empty.
