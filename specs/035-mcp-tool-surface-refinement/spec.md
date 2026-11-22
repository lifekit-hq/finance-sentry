# Feature Specification: MCP Tool Surface Refinement — Shape Over Count

**Feature Branch**: `035-mcp-tool-surface-refinement`
**Created**: 2026-07-22
**Status**: Implemented
**Input**: User description: "MCP tool surface refinement — shape over count. Encode call-order into tool descriptions, verify/fix runtime-broken tools, and make a few surgical merges while preserving read/write boundaries and narrow tool shape."

## Overview

The MCP server exposes ~57 tools. The felt problem is "too many," but the real problem is **shape and discoverability, not count**. The runtime lazy-loads tool schemas on demand, so the context cost of a large catalog is largely already paid, and feature 033's `run_analytics_query` once relieved read-side sprawl pressure (033 is superseded: the tool was removed in #1508, so each new read needs a curated tool).

This feature improves how the agent *selects and sequences* tools, fixes any tool that fails when invoked, and makes a small number of surgical merges — **without** collapsing the catalog into fat union-parameter tools (a "resource/action" mega-switch is harder to call correctly than many narrow, well-named tools and produces silent wrong-enum errors). Success is measured by the agent picking and ordering the right tool, not by a smaller number.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The agent discovers call-order from the tools themselves (Priority: P1)

The consuming agent (Ledger) answers a market/research question and knows — from the tool descriptions alone, not from external prompt prose — which tool to call first and what to call next. Where a tool is the intended entry point for a workflow, or is normally followed by another, its own description says so.

**Why this priority**: This is the highest-leverage, lowest-cost change. Today the agent's `AGENTS.md` prose compensates for undiscoverable tools (e.g. "call `get_radar_summary` first for any market question"). Moving that guidance into the tool descriptions means every MCP client — not just the one agent whose prompt was hand-tuned — gets correct sequencing for free, and the guidance can't drift out of sync with the tools.

**Independent Test**: Read the descriptions of the workflow-entry tools and their typical follow-ups; confirm each states its role in the sequence ("start here for X", "call after Y", "returns the same data as Z plus …"). Confirm the agent-side prose that duplicated this ordering can be removed without losing the behavior.

**Acceptance Scenarios**:

1. **Given** a workflow that has an intended first call (e.g. the market-structure entry point), **When** the agent reads that tool's description, **Then** it states the tool is the entry point for that class of question.
2. **Given** two tools normally invoked in sequence, **When** the agent reads the first, **Then** its description names the natural follow-up (or the follow-up is folded in — see US3).
3. **Given** the workflow guidance previously lived only in agent prose, **When** this feature is complete, **Then** that guidance exists in the tool descriptions and the duplicated prose can be deleted.

---

### User Story 2 - Every advertised tool actually works when called (Priority: P1)

No tool that appears in the catalog fails at runtime. A tool that errors on invocation is worse for trust than a dozen extra working tools, because the agent (and Denys) stop trusting the surface.

**Why this priority**: `get_pending_companion_events` was observed throwing a server error in production. The likely root cause — the Companion module was missing from the MCP process's module-registration list, so its tool handlers were never wired up — was already fixed in feature 033 / PR #297. This feature must **verify** that fix resolved it after deploy and **sweep** the whole surface for any other tool that fails when actually invoked.

**Independent Test**: Invoke every tool in the catalog against the deployed MCP server with a valid identity and representative arguments; confirm none returns a server/wiring error (a well-formed empty or "no data" result is a pass; an unhandled failure is not).

**Acceptance Scenarios**:

1. **Given** the deployed MCP server after PR #297, **When** `get_pending_companion_events` (or its successor) is invoked with a valid identity, **Then** it returns a normal result, not a server error.
2. **Given** the full tool catalog, **When** each tool is invoked once with representative arguments, **Then** every tool returns a well-formed response and none fails due to missing handler registration or wiring.
3. **Given** a tool is found broken, **When** the sweep completes, **Then** the failure and its root cause are recorded and fixed within this feature.

---

### User Story 3 - Surgical merges for always-paired and homogeneous-CRUD tools (Priority: P2)

A small number of tools that are *always called together*, or that are homogeneous CRUD over a *single* resource with a tiny closed action set, are merged — reducing selection friction without hiding heterogeneous behavior behind a mega-switch.

**Why this priority**: Real friction, but narrower than the count suggests. Valuable after US1/US2 because description quality and correctness matter more than the merges. Each change must preserve every existing capability. **Scope (decided):** exactly one true merge — watchlist CRUD (`list`/`add`/`remove`) → one action tool (57→55) — plus one enrichment — `run_thesis_monitor` also returns breaks, with `list_thesis_breaks` kept. The companion cluster is excluded (two n=2 read/write pairs).

**Independent Test**: For each merged tool, confirm the pre-merge capabilities are all still reachable and the old always-paired two-call dance is now one call; confirm the tool-name contract test reflects the new surface exactly.

**Acceptance Scenarios**:

1. **Given** the monitor-then-list-breaks pair that the agent always calls back-to-back, **When** `run_thesis_monitor` is enriched to also return the resulting breaks, **Then** the common case is one call — and `list_thesis_breaks` remains available as the side-effect-free read (no capability lost, boundary preserved).
2. **Given** watchlist read/add/remove over the one watchlist resource, **When** collapsed into one tool with an action parameter, **Then** all three operations remain available and the action set is small, closed, and unambiguous.
3. **Given** the companion notification cluster (`get`/`set_notification_mode` and `get_pending`/`acknowledge_companion_events`), **When** evaluated, **Then** it is left UNMERGED — it is two read/write pairs (n=2 each), not homogeneous CRUD on one resource, so the same rule that keeps IPS/risk separate applies.
4. **Given** the merges are applied, **When** the surface is inspected, **Then** the tool-name contract test matches the new set exactly (no more, no fewer) and no capability regressed.

---

### Edge Cases

- **Merge would mix read and write across a safety boundary** → do NOT merge; keep the read tool and the write tool separate (see Assumptions / Notes).
- **Merge would combine different resources** → do NOT merge; that produces a fat union-param tool and silent wrong-argument errors.
- **A tool's description implies ordering it can't guarantee** (e.g. "call after Y" but Y is optional) → describe the dependency as conditional, not absolute.
- **An external client already depends on a renamed/merged tool** → renamed/removed tool names are a breaking change to the tool contract; the change set must update the contract test and any agent-side references in the same change.
- **A tool returns empty legitimately** → an empty/"no data" result is a pass in the runtime sweep, never treated as a failure.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Tool descriptions MUST encode workflow sequencing where it exists — a tool that is the intended entry point for a class of question MUST say so, and a tool normally followed by another MUST name the follow-up (unless the follow-up is folded into it per FR-007).
- **FR-002**: Workflow/call-order guidance that currently lives only in agent-side prose MUST be relocated into the relevant tool descriptions, and the now-redundant prose MUST be removable without behavior loss.
- **FR-003**: Every tool in the catalog MUST return a well-formed response when invoked with a valid identity and representative arguments; no tool may fail due to missing handler registration or process wiring.
- **FR-004**: The fix for the observed `get_pending_companion_events` failure MUST be verified against the deployed server; the full surface MUST be swept for any other invocation-time failure, and each finding fixed within this feature.
- **FR-005**: The always-paired monitor→breaks two-call dance MUST collapse to one call by **enriching** `run_thesis_monitor` to also return the resulting breaks. The pure-read `list_thesis_breaks` MUST be KEPT (it re-evaluates nothing and fires no alerts) — the read/write boundary is preserved, per the same rule as FR-007. This is an enrichment, not a removal; it does not reduce tool count.
- **FR-006**: Homogeneous CRUD over a single resource with a small closed action set — specifically the watchlist (`list`/`add`/`remove`, 3 ops on one resource) — MAY be collapsed into one tool with an action parameter, provided every prior operation remains reachable and the action set is small and unambiguous.
- **FR-007**: Read/write tool pairs (n=2) that split a safe read from a mutation MUST NOT be merged. This covers `get_ips`/`save_ips`, `get_risk_rules`/`save_risk_rules`, AND the companion cluster's two pairs — `get_notification_mode`/`set_notification_mode` and `get_pending_companion_events`/`acknowledge_companion_events` — so "this tool never mutates" stays true at the tool boundary. n=2 read/write pairs are not sprawl.
- **FR-008**: The Radar entry-point-plus-drill-downs group MUST NOT be merged; one broad entry tool plus focused drill-downs is the intended shape.
- **FR-009**: No merge or rename may reduce expressiveness — every capability available before this feature MUST remain available after it.
- **FR-010**: The tool-name contract test MUST be updated in the same change to match the resulting surface exactly (no more, no fewer), and MUST continue to pass.
- **FR-011**: Tool count MUST NOT be treated as a success metric in its own right; the feature MUST NOT delete or merge tools solely to lower the number.

### Key Entities *(include if feature involves data)*

- **MCP Tool**: An individually named, narrowly scoped capability with a description that states its purpose, its arguments, and — new here — its place in any workflow sequence.
- **Tool Workflow**: An implied ordering across tools (entry point → follow-ups). Previously encoded in agent prose; now encoded in the tools' own descriptions.
- **Tool Surface Contract**: The authoritative set of tool names the server exposes, pinned by the contract test; the source of truth for "what exists."

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of tools that have an intended entry-point or follow-up role state that role in their own description (verifiable by inspection against the workflow map).
- **SC-002**: The workflow/call-order guidance duplicated in agent-side prose is removed, and the agent still sequences the affected workflows correctly using only tool descriptions.
- **SC-003**: 100% of catalog tools return a well-formed response when invoked with a valid identity; zero tools fail due to wiring/registration.
- **SC-004**: The previously broken companion-events tool (or its successor) returns a normal result on the deployed server.
- **SC-005**: Every always-paired or homogeneous-CRUD merge preserves all prior capabilities (zero capability regressions), verified operation-by-operation.
- **SC-006**: The tool-name contract test matches the final surface exactly and passes; the read/write safety pairs (IPS, risk rules) and the Radar group remain unmerged.

## Assumptions

- **The consuming agent is capable**: it selects tools from names + descriptions, so description quality is the primary lever for correctness — not count.
- **Lazy schema loading**: the MCP runtime loads tool schemas on demand, so a large catalog does not impose a large fixed context cost; this justifies optimizing for shape over count.
- **033 (superseded, #1508)** — the following no longer holds, `run_analytics_query` was removed: it absorbed novel one-off structured reads, so the catalog will not need a new read tool per question going forward.
- **Renames are breaking**: merged/renamed tool names change the tool contract; agent-side references and the contract test are updated in the same change; there are no unknown external consumers beyond the project's own agents.
- **Backend-only**: this is entirely within the MCP server + its module wiring; no frontend or data-model changes.
- **Companion fix already merged**: the DI-registration fix shipped in 033/PR #297; this feature verifies rather than re-implements it.

## Notes

- **[DECISION] Shape over count**: The guiding principle is that a tool is well-shaped when its name predicts exactly one behavior and its parameters carry no hidden mode switch. Merges are justified only when the union is tiny, closed, and over one resource. Count is explicitly a vanity metric (FR-011).
- **[DECISION] Preserve read/write boundaries**: `get_*`/`save_*` pairs that split a safe read from a mutation stay separate (FR-007) — the same read-vs-write axis feature 033 is built on. n=2 pairs are not sprawl.
- **[DECISION] Companion cluster stays unmerged (2026-07-22)**: Applying the n=2 rule consistently, the four companion tools are two read/write pairs (mode get/set; events get/acknowledge), not homogeneous CRUD on one resource — so they are NOT merged, same as IPS/risk.
- **[DECISION] Thesis: enrich, don't merge (2026-07-22)**: `run_thesis_monitor` (a write with alert side effects) is enriched to also return the resulting breaks so Ledger's back-to-back dance is one call; `list_thesis_breaks` (pure read, no re-eval, no alerts) is KEPT. Forcing every "what's broken?" through the write would spam alert state and lose the read — so the boundary is preserved, consistent with the companion/IPS/risk decisions. Net: the only count reduction is the watchlist merge (57→55).
- **[DECISION] Descriptions carry workflow**: Call-order belongs in the tool, not the prompt (FR-001/FR-002). This is the primary win of the feature; the merges are secondary.
- **[DECISION] Broken beats extra**: One invocation-time failure costs more trust than many extra working tools, so the runtime sweep (US2) is P1 alongside descriptions, ahead of the merges (P2).
- **[OUT OF SCOPE] Fat union-param "god tools"**: Collapsing heterogeneous resources/actions behind a single mega-switch is explicitly rejected — it degrades call-correctness.
- **[OUT OF SCOPE] Radar consolidation**: The Radar entry + five drill-downs stay as-is (FR-008).
- **[DEFERRED] Natural-language tool router**: Any auto-routing/meta-tool that picks tools for the agent is a separate future idea, not part of this feature.
- **Expected surface delta**: 57 → 55 (watchlist 3→1 is the only merge; the thesis change is an enrichment that keeps both tools). The number falls out of the change; it is not a target.
- **Delivery**: intended to be handed to DevClaw as one workstream, sequenced (1) verify/fix broken endpoint → (2) rewrite descriptions for call-order → (3) surgical merges + contract-test update.
