# Implementation Plan: MCP Tool Surface Refinement — Shape Over Count

**Branch**: `035-mcp-tool-surface-refinement` | **Date**: 2026-07-22 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/035-mcp-tool-surface-refinement/spec.md`

## Summary

Improve the MCP tool surface by **shape, not count**. Three workstreams: (US1) move workflow/call-order guidance out of agent prose and into the tool `[Description]`s so every client sequences correctly; (US2) guarantee no advertised tool fails at invocation — verify the 031/#297 companion fix and add a **DI-resolution smoke test** that constructs every tool from a fully-wired provider (the exact bug class #297 fixed becomes a test-time failure); (US3) one true merge — watchlist `list/add/remove` → a single `watchlist(action)` tool (57→55) — plus one enrichment — `run_thesis_monitor` also returns the resulting breaks while `list_thesis_breaks` stays as the side-effect-free read. Read/write boundaries (IPS, risk rules, companion cluster) and the Radar group are explicitly left alone.

## Technical Context

**Language/Version**: C# 13 / .NET 9
**Primary Dependencies**: `ModelContextProtocol` / `ModelContextProtocol.AspNetCore` (MCP SDK), `FinanceSentry.Core.Cqrs` (hand-rolled ICommand/IQuery — no MediatR). No new NuGet packages.
**Storage**: None new. The merged/enriched tools call existing Research module handlers (`GetWatchlistQuery`, `AddWatchlistItemCommand`, `RemoveWatchlistItemCommand`, `RunThesisMonitorCommand`, `ListThesisBreaksQuery`) — no schema or migration changes.
**Testing**: xUnit + FluentAssertions in `FinanceSentry.Mcp.Tests`. Existing `ToolNameContractTests` + `McpToolReflection` are updated; a new DI-resolution smoke test is added.
**Target Platform**: Linux server (Docker); MCP over stdio + streamable-HTTP.
**Project Type**: Backend MCP host only — no frontend, no data model.
**Performance Goals**: N/A (description edits + one tool composition). The enriched `run_thesis_monitor` does one extra read after the run — negligible.
**Constraints**: zero-warning build; renamed/removed tool names are a breaking contract change and MUST land with the contract-test update in the same change; every prior capability preserved (FR-009).
**Scale/Scope**: ~57 tool descriptions audited; 3 tools removed + 1 added (watchlist); 1 tool enriched (thesis monitor); 1 new smoke test.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Modular monolith / integration isolation**: PASS — no module boundaries touched; the merged tools call existing Research handlers through the CQRS bus exactly as the current tools do. MCP tools are the host surface, not a module.
- **II. Code quality (zero-warning build)**: PASS — enforced; the change is C# in `FinanceSentry.Mcp` + tests.
- **V. Security / per-user isolation**: PASS — every tool still resolves the caller via `IIdentityResolver`; the merge does not change identity handling. Read/write boundaries are *strengthened* (kept, not merged away).
- **Testing discipline**: PASS — contract test updated; new DI-resolution smoke test raises coverage of the exact failure mode (missing handler registration).
- No frontend principles (VI) apply — backend-only.

**No violations.** One minor, deliberate shape deviation (an `action` parameter on `watchlist`) is recorded in Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/035-mcp-tool-surface-refinement/
├── plan.md              # This file
├── research.md          # Phase 0 — the description/workflow map + merge contracts
├── data-model.md        # Phase 1 — the WatchlistToolResult + enriched monitor DTOs
├── quickstart.md        # Phase 1 — how to verify (sweep + contract test + manual invoke)
├── contracts/
│   └── mcp-tools.md      # Phase 1 — before/after tool contracts
└── tasks.md             # Phase 2 (/speckit.tasks)
```

### Source Code (repository root)

```text
backend/src/FinanceSentry.Mcp/Tools/
├── WatchlistTool.cs                 # NEW — watchlist(action: list|add|remove, ...)
├── ListWatchlistTool.cs             # REMOVED
├── AddToWatchlistTool.cs            # REMOVED
├── RemoveFromWatchlistTool.cs       # REMOVED
├── RunThesisMonitorTool.cs          # ENRICHED — also returns resulting breaks
├── GetRadarSummaryTool.cs           # DESC — mark as the entry point for market questions
├── GetMarketStructureTool.cs        # DESC — "drill-down; start from get_radar_summary"
├── GetMarketBreadthTool.cs          # DESC — ditto
├── GetRelativeStrengthTool.cs       # DESC — ditto
├── GetSectorRotationTool.cs         # DESC — ditto
├── PromoteCandidateTool.cs          # DESC — "calls check_risk_rules first (019 gate)"
├── …                                # DESC — audit the rest for entry/follow-up ordering
└── (the 033 analytics tools were removed in #1508)

backend/src/FinanceSentry.Mcp/API/Responses/   (or Tools/ local)
└── WatchlistToolResult.cs           # NEW — union-shaped result for the 3 actions
└── ThesisMonitorResult.cs           # NEW/ENRICHED — summary + breaks

backend/tests/FinanceSentry.Mcp.Tests/
├── ContractTests/ToolNameContractTests.cs      # UPDATE — 57 → 55 surface
└── ContractTests/ToolResolutionTests.cs        # NEW — construct every tool from a wired provider
```

**Structure Decision**: All changes live in `FinanceSentry.Mcp` (host) + `FinanceSentry.Mcp.Tests`. No module, DB, or frontend changes. The merged tool's result DTOs live beside the tool (or under `API/Responses/` if that folder is the Mcp convention — confirm in Phase 1).

## Key design decisions (for /speckit.tasks)

1. **Descriptions are the primary deliverable (US1).** Audit every tool's `[Description]`. For each workflow, the entry tool says "start here for <class of question>" and drill-downs say "drill-down; call `<entry>` first." Concrete edits: `get_radar_summary` = entry for any market/narration question; the four market drill-downs point back to it; `promote_candidate` states it runs `check_risk_rules` as a hard gate; the 033 analytics pair already cross-references. **No logic changes — string edits only.** The matching removal of duplicated prose lives in Ledger's persona on the VPS (out of repo) — the repo change makes the descriptions self-sufficient so that removal is safe; flag it as a deploy-side follow-up in quickstart.

2. **DI-resolution smoke test (US2) is the durable guard.** New `ToolResolutionTests`: build a `ServiceCollection`, run the same registration `RegisterShared` uses (all module registrars + CQRS + identity), then for every `[McpServerToolType]` in the MCP assembly, `ActivatorUtilities`/`GetRequiredService` the tool and assert it constructs. A missing handler registration (the Companion/#297 bug) fails this test deterministically — converting a production 500 into a red test. This is the highest-value part of US2 and is CI-able (no live server needed).

3. **Verify the companion fix (US2).** After the smoke test is green, confirm on the deployed server that `get_pending_companion_events` returns normally (manual/agent invoke — documented in quickstart). The code fix already shipped in #297; this feature proves it.

4. **Watchlist merge (US3).** New `WatchlistTool` with `[McpServerTool(Name="watchlist")]` and `action` ∈ {`list`,`add`,`remove`}. Injects the three existing handlers. Params: `action` (required), `ticker`/`exchange`/`note` (for add), `itemId` (for remove), `userId` (optional). Returns `WatchlistToolResult` (see data-model). Validates required params per action, returns a clear error on mismatch. Delete the three old tools. Every prior operation stays reachable (FR-009).

5. **Thesis enrichment, not merge (US3).** `RunThesisMonitorTool` injects the `ListThesisBreaksQuery` handler in addition to the monitor command handler; after running the monitor it queries the breaks and returns `ThesisMonitorResult { summary, breaks }`. `list_thesis_breaks` is untouched (pure read, no side effects). Count unchanged.

6. **Contract test update (US3).** `ToolNameContractTests` agreed surface: remove `list_watchlist`/`add_to_watchlist`/`remove_from_watchlist`, add `watchlist` → 55. The "because" message updates to 55.

## Complexity Tracking

| Violation | Why needed | Simpler alternative rejected because |
|---|---|---|
| `action` parameter on the `watchlist` tool (a mild "mode switch", against the narrow-tool preference) | Watchlist is one resource with the canonical CRUD triad (list/add/remove); Ledger cited it as the legit collapse and Denys confirmed it. The action set is 3, closed, and unambiguous | Keeping three separate tools is defensible but is the one homogeneous-single-resource case where the collapse genuinely reduces selection friction without hiding heterogeneous behavior. The union return is small and documented. The companion cluster (two resources) and read/write pairs were correctly NOT collapsed for exactly this reason |
