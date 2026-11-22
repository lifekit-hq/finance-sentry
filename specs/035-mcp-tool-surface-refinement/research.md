# Research: MCP Tool Surface Refinement

Backend/MCP only. No new dependencies. This records the decisions that resolve the plan's design points.

## D1 — What "encode call-order in descriptions" means concretely

**Decision**: Edit `[Description]` strings only; no logic change. Add an explicit sequencing sentence to any tool that is a workflow entry point or a drill-down/follow-up.

**Workflow map (initial — confirm during implement by grepping AGENTS.md-style prose):**

| Workflow | Entry tool | Follow-ups / drill-downs | Description edit |
|---|---|---|---|
| Market / narration | `get_radar_summary` | `get_market_structure`, `get_market_breadth`, `get_relative_strength`, `get_sector_rotation` | Entry says "first call for any market/narration question"; each drill-down says "drill-down — call `get_radar_summary` first for the overview." |
| Candidate → position | `score_candidate` / `list_candidates` | `promote_candidate` → `check_risk_rules` | `promote_candidate` states it invokes `check_risk_rules` as a hard gate (019). |
| Thesis health | `run_thesis_monitor` (now returns breaks) | `list_thesis_breaks` (pure read) | `run_thesis_monitor` says "re-evaluates + returns current breaks; for a read-only view without re-eval use `list_thesis_breaks`." |
| ~~Ad-hoc structured query~~ | ~~`describe_query_schema`~~ | ~~`run_analytics_query`~~ | Both tools removed (033 superseded, #1508). |

**Rationale**: Descriptions travel with the tool to every client; agent prose does not and drifts. **Alternatives rejected**: keeping guidance in Ledger's persona (invisible to other clients, drifts out of sync).

**Boundary**: The duplicated prose lives in Ledger's persona on the VPS (out of this repo). The repo change makes descriptions self-sufficient; trimming the persona is a deploy-side follow-up noted in quickstart, not a repo task.

## D2 — Guarding against broken tools

**Decision**: Add `ToolResolutionTests` — a DI smoke test that builds the real MCP service graph (same registrations as `Program.RegisterShared`) and constructs every `[McpServerToolType]`. Assert all resolve.

**Rationale**: The `get_pending_companion_events` 500 was a missing handler registration (Companion absent from `moduleAssemblies`, fixed in #297). A resolution test turns that whole bug class into a deterministic red test, runnable in CI with no live server. **Alternatives considered**: live end-to-end invoke of every tool — valuable but needs a running server + seeded data + identity; kept as a manual quickstart step, not the CI gate.

**Note**: `RegisterShared` currently lives inline in `Program.cs` as a local function. To reuse it from the test, extract the registration into a small static helper (e.g. `McpServiceRegistration.RegisterShared(services, config, moduleAssemblies, mcpAssembly)`) that both `Program.cs` and the test call. Pure refactor, no behavior change.

## D3 — Watchlist merge shape

**Decision**: One `watchlist` tool, `action` ∈ {list, add, remove}. Injects the three existing Research handlers (`GetWatchlistQuery`, `AddWatchlistItemCommand`, `RemoveWatchlistItemCommand`). Returns a small union DTO (`WatchlistToolResult`). Per-action required params validated; a clear error DTO on mismatch (e.g. `add` without `ticker`).

**Rationale**: One resource, closed 3-action set — the sanctioned homogeneous-CRUD collapse. **Alternatives rejected**: separate read tool + `watchlist_edit(add/remove)` (splits the canonical triad for marginal purity); status-quo three tools (the one case where collapse genuinely helps selection).

## D4 — Thesis: enrich, don't merge

**Decision**: `RunThesisMonitorTool` additionally injects the `ListThesisBreaksQuery` handler; after the monitor command it queries breaks and returns `ThesisMonitorResult { summary, breaks }`. `ListThesisBreaksTool` stays.

**Rationale**: `run_thesis_monitor` is a write with alert side effects; forcing every "what's broken?" through it would spam alert state and lose the pure read. Enriching the write's output collapses the common two-call path without sacrificing the boundary. **Alternatives rejected**: full merge dropping `list_thesis_breaks` (loses the side-effect-free read — FR-009 violation).

## D5 — Contract surface

**Decision**: 57 → 55. Remove `list_watchlist`, `add_to_watchlist`, `remove_from_watchlist`; add `watchlist`. Everything else unchanged; companion cluster, IPS/risk pairs, and Radar six all retained.
