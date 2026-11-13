# Plan: Action Tickets (044)

## Architecture decisions

| Decision | Choice | Why |
|---|---|---|
| Job home | `Modules.Research/Infrastructure/Jobs/ActionTicketsGeneratorJob.cs` | Research already owns IPS + `GetAllocationDriftQueryHandler`; `IAlertGeneratorService` in Core is already reachable; no new project reference |
| Alert representation | Existing `Alert` entity, Message = formatted order list string | US1 needs no new DB table; structured order data (US3+) can come later when the accept endpoint needs it |
| User iteration | `IIpsRepository.GetUserIdsWithCurrentIpsAsync()` | Same precedent as `OpportunityScanJob` — only users with an IPS get proposals |
| Drift source | Inject `IQueryHandler<GetAllocationDriftQuery, AllocationDriftDto>` | CQRS handlers are auto-registered; already used in MCP tool + Integration adapter |
| Alert dedup referenceId | MD5(`"rebalance:portfolio:{userId}"`) | Portfolio-wide alert needs stable per-user anchor, no natural entity reference; mirrors `ViolationReferenceId` pattern |
| Silence window | One notification per drift episode; 7 days for an accepted, dismissed or deferred proposal | The drift signature in `alerts.alerts."DedupKey"` and the episode's high-water mark decide a repeat (spec FR-004, #1266); the earlier 24 h window re-notified a persisting drift daily |
| Cron slot | `Cron.Daily(4)` (04:00 UTC) | After PortfolioScanner (02:00) and Research macro (03:00) |
| Order sizing | `|ActualValueUsd − TargetPct% × TotalUsd|` | Direct from drift DTO; no additional API calls |

## US1 surface — files touched / created

- `FinanceSentry.Modules.Alerts/Domain/AlertType.cs` — add `RebalanceProposal`
- `FinanceSentry.Modules.Companion/Domain/CompanionEventKind.cs` — add `RebalanceProposal`
- `FinanceSentry.Modules.Companion/Application/Services/MaterialityPolicy.cs` — add switch case
- `FinanceSentry.Core/Interfaces/IAlertGeneratorService.cs` — add `GenerateRebalanceProposalAlertAsync`
- `FinanceSentry.Modules.Alerts/Application/Services/AlertGeneratorService.cs` — implement it
- `FinanceSentry.Modules.Research/Infrastructure/Jobs/ActionTicketsGeneratorJob.cs` — new job (create)
- `FinanceSentry.Modules.Research/ResearchModule.cs` — register job + DI
- `tests/.../Opportunity/OpportunityFakes.cs` — stub new method in `FakeOpportunityAlertGenerator`
- `tests/.../ThesisMonitor/RunThesisMonitorHandlerTests.cs` — stub new method in `FakeAlertGeneratorService`
- `tests/FinanceSentry.Tests.Unit/Research/ActionTicketsGeneratorJobTests.cs` — unit tests (create)

## US2 surface

- `IAlertGeneratorService.GenerateCashSweepProposalAlertAsync` + `AlertGeneratorService` implementation (24h silence, `CashSweepReferenceId` MD5 anchor)
- `CashSweepProposal` AlertType + CompanionEventKind + MaterialityPolicy mapping
- `ActionTicketsGeneratorJob.TryGenerateCashSweepAsync` — injects `IQueryHandler<GetRiskRuleSetQuery, RiskRuleSetDto?>` directly (Research → Risk project reference; no cycle; both only depend on Core)
- `FinanceSentry.Modules.Research.csproj` → adds `<ProjectReference>` to Risk
- `FinanceSentry.Modules.Research.Tests.csproj` → adds `<ProjectReference>` to Risk for mock setup
- `tests/.../Jobs/ActionTicketsGeneratorJobTests.cs` — 6 new cash-sweep tests

## US3 surface

- `Alert.AcknowledgementDecision` + `Alert.AcknowledgedAt` (nullable) + `AlertsDbContext` config + migration `M003_AddAcknowledgement`
- `IAlertRepository.AcknowledgeAsync` (by alert row id) + `AcknowledgeByReferenceAsync` (by stable anchor — bot path)
- `AcknowledgeProposalCommand` + `AcknowledgeProposalByReferenceCommand` + handlers in Alerts module
- `PATCH /alerts/{id}/acknowledge` (frontend) + `PATCH /alerts/by-reference/{referenceId}/acknowledge` (Telegram bot)
- `WebhookAgentWakeDispatcher.WakeAsync` — adds `requiresAcknowledgement: true` + `referenceId` to payload for `RebalanceProposal`/`CashSweepProposal` events; bot uses referenceId to call the by-reference endpoint

## US4 surface (future slice)

- New `get_action_tickets` MCP tool in `FinanceSentry.Mcp/Tools/`
- Reads open (unresolved, unread) RebalanceProposal + CashSweepProposal alerts
- Returns: alertId, type, title, message, createdAt
