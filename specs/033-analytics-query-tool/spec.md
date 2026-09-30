# Feature Specification: Read-Only Analytical Query Tool

**Feature Branch**: `033-analytics-query-tool`
**Created**: 2026-07-22
**Status**: Implemented
**Input**: User description: "Give the agent one guarded read-only query tool so it can answer novel structured questions by querying curated per-user views directly, instead of needing a bespoke tool per question — without ever being able to write, read another user's data, or run away with an expensive query."

## Overview

Finance Sentry exposes ~55 typed tools. That's the right model for load-bearing, must-be-correct operations — but it means the agent can only answer questions someone pre-built a tool for. Novel structured questions ("weeks my discretionary spend was 30% above my 3-month average", "holdings down >10% this month that I've held over a year") have no tool, so the answer is "I can't" until a feature ships.

This feature adds **one escape-hatch tool**: a guarded, **read-only** analytical query surface over a small set of **curated, per-user views**. It attacks tool sprawl (the long tail stops needing bespoke tools) while keeping the system's core guarantee intact — this is explicitly for the *exploratory long tail*, NOT for load-bearing numbers, which stay as deterministic bespoke tools. Safety is enforced in layers at the database and validation level, not by asking the agent nicely.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Answer a novel structured question safely (Priority: P1)

The agent composes a read-only SQL query against curated per-user views to answer a question no bespoke tool covers, and gets back the result rows plus the exact SQL it ran. The query cannot write, cannot touch another user's data, cannot run past a time/row budget, and cannot reach anything but the curated views.

**Why this priority**: This is the whole feature — the flexible escape hatch — and its value is inseparable from its safety. Delivering it alone gives the agent a large new answering surface without a new tool per question.

**Independent Test**: Ask a question with no dedicated tool; confirm the agent returns correct rows + the SQL; confirm a write/DDL attempt is rejected; confirm a query for another user's data returns only the caller's rows; confirm a runaway query is cut off by the timeout/row cap.

**Acceptance Scenarios**:

1. **Given** a novel analytical question, **When** the agent runs a read-only `SELECT` over the curated views, **Then** it receives the result rows and the exact SQL executed.
2. **Given** any attempt to write (INSERT/UPDATE/DELETE/DDL) or run multiple statements, **When** submitted, **Then** it is rejected before execution and nothing is mutated.
3. **Given** a query that references another user's data or a non-curated table, **When** submitted, **Then** it returns only the caller's data (or is rejected) — never another user's rows.
4. **Given** a query that would scan too long or return too many rows, **When** run, **Then** it is stopped by a statement timeout and a row cap, with a clear "too large — narrow it" message.
5. **Given** any query, **When** it completes, **Then** the response includes the SQL that ran so the agent can cite it and Denys can audit it.

---

### User Story 2 - Know what's queryable (Priority: P2)

The agent can discover the available views and their columns (a "schema card"), so it writes correct queries instead of guessing at the schema.

**Why this priority**: Reliable queries depend on the agent knowing the exact surface. Without it, query quality degrades and it guesses wrong. Depends on US1 (there must be a query surface to describe).

**Independent Test**: Request the queryable schema; confirm it lists exactly the curated views with their columns and short descriptions, and nothing else (no raw internal tables).

**Acceptance Scenarios**:

1. **Given** the query surface, **When** the agent requests the schema, **Then** it receives the curated views, their columns/types, and a one-line purpose for each.
2. **Given** the schema, **When** compared to the database, **Then** it exposes only the curated per-user views — never raw internal tables.

---

### User Story 3 - Every query is auditable (Priority: P3)

Each executed query is recorded (who, when, the SQL, row count, duration) so Denys can review what the agent asked and catch a query that produced a subtly wrong answer.

**Why this priority**: Because the agent can write a *valid-but-wrong* query and state the result confidently, an audit trail is the safety net that makes the trade-off acceptable. Valuable hardening, but the capability works without it.

**Acceptance Scenarios**:

1. **Given** an executed query, **When** it finishes, **Then** a record is written with the caller, timestamp, SQL, row count, and duration.
2. **Given** a rejected query, **When** blocked, **Then** the rejection and reason are recorded too.

---

### Edge Cases

- **Valid-but-wrong query**: the agent writes a syntactically fine query that misreads the schema → returns wrong rows confidently. Mitigated by clean/obvious views, returning the SQL for transparency, and keeping load-bearing answers on bespoke tools (not this).
- **Expensive query**: a cartesian join or full scan → statement timeout + row cap stop it; the agent is told to narrow.
- **Injection / multi-statement**: `;`-separated statements, comment tricks, CTEs that write → rejected by single-`SELECT` validation *and* by the read-only role (defense in depth).
- **Empty result**: honest "0 rows", never a fabricated answer.
- **No identity**: an unauthenticated call is rejected — no default user.
- **Schema drift**: a curated view changes → the schema card reflects the current views, never a stale hardcoded copy.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST expose a single tool that executes a caller-supplied **read-only** query and returns the result rows plus the exact SQL executed.
- **FR-002**: Execution MUST be read-only enforced at the database level (a `SELECT`-only role/connection) — not solely by validation. Writes and DDL MUST be impossible even if validation is bypassed.
- **FR-003**: Only a curated set of **per-user views** MUST be queryable; raw internal tables MUST NOT be reachable through this tool.
- **FR-004**: Every query MUST be scoped to the authenticated caller's data; it MUST be impossible to return another user's rows. Calls without a valid identity MUST be rejected.
- **FR-005**: The system MUST accept only a single `SELECT` statement; multiple statements, writes, and DDL MUST be rejected before execution. A query MUST call only allowlisted functions (aggregate, window, math, date/time, text, array); any other call — `set_config`, `current_setting`, `pg_*`, schema-qualified — MUST be rejected, so submitted SQL cannot re-point its own owner scope.
- **FR-006**: The system MUST bound each query with a statement timeout and a maximum row count, returning a clear "too large — narrow it" outcome rather than hanging or dumping unbounded rows.
- **FR-007**: The agent MUST be able to discover the queryable surface (the curated views, their columns/types, and a one-line purpose each).
- **FR-008**: Each executed or rejected query MUST be recorded for audit (caller, timestamp, SQL, outcome, row count, duration).
- **FR-009**: This tool MUST NOT be the source for load-bearing/authoritative numbers (net worth, risk verdicts, holdings totals) — those remain dedicated deterministic tools. The tool's description MUST frame it as the exploratory/long-tail surface.

### Key Entities *(include if feature involves data)*

- **Curated Query View**: a per-user, read-optimized, documented view (e.g. transactions, holdings, analyst actions, net-worth-daily) — the only surface the tool can read. Denormalized, human-named columns.
- **Query Audit Record**: one executed/rejected query — caller, timestamp, SQL text, outcome (executed/rejected + reason), row count, duration.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The agent can answer a structured question that has **no dedicated tool**, returning correct rows and the SQL, in a single tool call.
- **SC-002**: **100%** of write/DDL/multi-statement attempts are rejected and nothing is mutated (verified by attempt + inspection).
- **SC-003**: **Zero** cross-user data exposure — a query can never return another user's rows (verified with a second user's data present).
- **SC-004**: A deliberately runaway query is stopped by the timeout/row cap **every** time, with a clear narrow-it message.
- **SC-005**: The queryable schema the agent sees matches the actual curated views and excludes all raw internal tables.
- **SC-006**: **100%** of queries (executed and rejected) appear in the audit trail.

## Assumptions

- **Postgres roles/views are the enforcement mechanism**: a `SELECT`-only role plus per-user-filtered views is the primary guard; validation is a second layer. This suits the existing PostgreSQL 14 stack.
- **The agent writes the SQL**: it is a capable model; the system is the safety layer, not a natural-language-to-SQL translator. (A NL-to-SQL layer can be added later but is out of scope.)
- **Curated views are a small, deliberate set** covering the highest-value structured data (transactions, holdings, analyst actions, net-worth history, budgets) — grown as needed, not an auto-exposed schema.
- **Single primary user** in practice; per-user scoping is still enforced for correctness.
- **Read replica optional**: same-DB read-only role is acceptable at current scale; a replica can be introduced later if load warrants.

## Notes

- **[DECISION] Escape hatch, not a replacement**: this complements the bespoke tools, it does not replace them. Load-bearing numbers stay deterministic (FR-009); this covers the exploratory long tail and directly relieves tool sprawl.
- **[DECISION] Safety in layers**: read-only DB role (can't write) + curated per-user views (can't reach raw/other-user data) + single-SELECT validation + timeout/row cap + audit trail. No single layer is trusted alone.
- **[OUT OF SCOPE] Natural-language-to-SQL translation**: the agent supplies SQL; an NL→SQL front-end is a possible later addition.
- **[OUT OF SCOPE] RAG / text search**: semantic search over unstructured text (news, filings, notes) is a separate feature — this tool is for structured/numeric data only. They are complementary (numbers vs. prose), not alternatives.
- **[DEFERRED] Read replica**: introduce if query load affects the primary.
