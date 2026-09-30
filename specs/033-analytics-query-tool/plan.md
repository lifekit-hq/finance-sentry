# Implementation Plan: Read-Only Analytical Query Tool

**Branch**: `033-analytics-query-tool` | **Date**: 2026-07-22 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/033-analytics-query-tool/spec.md`

## Summary

A new **`Analytics` module** exposes one guarded MCP tool, `run_analytics_query`, that runs a caller-supplied **read-only `SELECT`** over a small set of **curated per-user views** and returns rows + the exact SQL. Safety is enforced in layers: a dedicated **`SELECT`-only Postgres role**, **Row-Level Security** pinned to the caller (so cross-user reads are impossible at the DB), a **single-`SELECT` validator**, a **statement timeout + row cap**, and an **audit table**. A companion `describe_query_schema` tool returns the curated views + columns so the agent writes correct SQL. This is the exploratory long-tail surface — load-bearing numbers stay on bespoke deterministic tools.

## Technical Context

**Language/Version**: C# 13 / .NET 9
**Primary Dependencies**: ASP.NET Core 9, EF Core 9 / Npgsql (raw `SELECT` executed on a dedicated read-only connection), `FinanceSentry.Core.Cqrs` (hand-rolled ICommand/IQuery — no MediatR), `ModelContextProtocol` SDK. No new NuGet packages.
**Storage**: PostgreSQL 14 — a new **`analytics` schema**: curated **views** (over existing module tables, read-only), a `query_audit` table, RLS policies; plus a **`fs_readonly` role** (SELECT-only) + a dedicated read-only connection string. Views + role + RLS created via migration `M001`.
**Testing**: xUnit + FluentAssertions. Unit tests for the SQL validator + per-user scoping; integration tests against a throwaway Postgres for the read-only role + RLS (cross-user isolation) and timeout/row-cap.
**Target Platform**: Linux server (Docker); single-node.
**Project Type**: Backend modular monolith + MCP (backend only — no frontend).
**Performance Goals**: statement timeout ~5s; row cap ~1000; one audit write per call.
**Constraints**: no writes possible even if validation is bypassed (role-enforced); no cross-user rows (RLS-enforced); zero-warning build; migration ships with `.Designer.cs`.
**Scale/Scope**: single primary user; a handful of curated views to start.

## Constitution Check

- **I. Modular isolation / contracts**: PASS *with a documented, deliberate exception* — the curated views read other modules' tables. This is a recognized **read-model / reporting layer** pattern, not code coupling: read-only, versioned in one migration, owned by Analytics, and the *only* cross-schema reach. Recorded in Complexity Tracking.
- **II. CQRS hand-rolled**: PASS — `RunAnalyticsQuery`/`DescribeQuerySchema` as `IQuery` handlers.
- **III. Per-user isolation**: PASS — enforced at the DB by **RLS** (session variable set per query), not by the agent's SQL.
- **IV. One concept per file**: PASS.
- **Testing discipline**: PASS — validator + scoping unit tests; role/RLS/timeout integration tests. No new external source.
- **Migration discipline**: PASS — `M001` with `.Designer.cs`; it also creates the role + RLS (raw SQL in the migration).
- **Zero-warning build**: PASS.

## Project Structure

```text
backend/src/FinanceSentry.Modules.Analytics/          # NEW module
├── AnalyticsModule.cs                                # DI + registration
├── Application/
│   ├── Queries/{RunAnalyticsQuery,DescribeQuerySchema}.cs
│   └── Services/
│       ├── ISqlGuard.cs + SqlGuard.cs                # single-SELECT validation (reject writes/DDL/`;`)
│       ├── IReadOnlyQueryExecutor.cs + ReadOnlyQueryExecutor.cs  # fs_readonly conn, RLS session var, timeout, row cap
│       ├── ICuratedSchema.cs + CuratedSchema.cs      # the curated view list + columns (schema card)
│       └── AnalyticsOptions.cs                       # ReadOnlyConnectionString, StatementTimeoutMs, MaxRows
├── Domain/QueryAuditRecord.cs
├── Infrastructure/Persistence/{AnalyticsDbContext,AnalyticsDbContextFactory}.cs   # audit table + migration home
├── Infrastructure/Persistence/Repositories/QueryAuditRepository.cs
└── Migrations/…_M001_InitialSchema{,.Designer}.cs    # analytics schema: views + query_audit + fs_readonly role + RLS

backend/src/FinanceSentry.Mcp/Tools/
├── RunAnalyticsQueryTool.cs
└── DescribeQuerySchemaTool.cs

backend/tests/FinanceSentry.Modules.Analytics.Tests/  # NEW test project
└── {SqlGuardTests, ReadOnlyExecutorTests, CuratedSchemaTests}.cs
```

**Structure Decision**: A dedicated `FinanceSentry.Modules.Analytics` module (mirrors Risk/Companion). It owns the read layer (views + role + RLS + audit) and the two MCP tools. The read-only connection uses `fs_readonly`; the app's normal role is unaffected.

## Key design decisions (for /speckit.tasks)

1. **Read-only enforced by a DB role, not validation.** `M001` creates role `fs_readonly` with `GRANT SELECT` on the curated views only — no write grants, no base-table grants. The executor connects as that role. A validator bypass still cannot write.
2. **Per-user isolation by RLS.** The executor runs `SET LOCAL app.current_user_id = <caller>` in the query transaction; the curated views are security-barrier views filtering on `current_setting('app.current_user_id')` (and/or RLS on base tables). The agent's SQL cannot widen this.
3. **Validator (defense in depth).** `SqlGuard`: exactly one statement, must be `SELECT`/`WITH…SELECT` (no data-modifying CTE), no `;` chaining, no DDL/DML, function calls limited to an allowlist (no `set_config`/`current_setting`/`pg_*`). Reject before execution.
4. **Timeout + row cap.** `SET LOCAL statement_timeout`; enforce `MaxRows`; return a clear "too large — narrow it" outcome.
5. **Curated views v1** (small, per-user, documented): `v_transactions`, `v_holdings`, `v_analyst_actions`, `v_net_worth_daily`, `v_budgets`. Grown as needed.
6. **Schema card** = `CuratedSchema` returns view list + columns + one-line purpose; `describe_query_schema` surfaces it; also embedded in the run tool description.
7. **Audit** = every executed/rejected query → `query_audit` (caller, ts, sql, outcome+reason, rowCount, durationMs), written on the app's normal (writable) connection.

## Complexity Tracking

| Violation | Why needed | Simpler alternative rejected because |
|---|---|---|
| Cross-module read views (Analytics reads other schemas' tables) | The whole point is flexible querying across the user's data; a per-module query tool reproduces the tool-sprawl this feature removes | Confining it to one module's data makes the escape-hatch useless; the read-only view layer is a standard reporting pattern — read-only + versioned + RLS-scoped |
