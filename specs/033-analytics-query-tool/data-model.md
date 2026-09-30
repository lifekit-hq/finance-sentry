# Data Model: Read-Only Analytical Query Tool

New objects live in a new **`analytics`** schema (migration `M001`, with `.Designer.cs`). Two kinds of objects: **curated views** (the queryable read surface) and one **audit table**. Plus a **`fs_readonly` role** + **RLS** (created via raw SQL in the migration).

## Curated views (v1 — the only queryable surface)

Security-barrier, per-user views. Each filters on `current_setting('app.current_user_id')::uuid` so a query only ever sees the caller's rows. Denormalized, human-named columns.

| View | Purpose | Key columns (indicative) |
|---|---|---|
| `analytics.v_transactions` | Bank/card transactions | `date, amount, currency, merchant, category, account_name, direction` |
| `analytics.v_holdings` | Current holdings across brokerage + crypto | `symbol, asset_class, quantity, market_value_usd, cost_basis_usd, account` |
| `analytics.v_analyst_actions` | Street actions (market-wide) | `ticker, firm, action_type, prior_rating, new_rating, prior_target, new_target, action_date` |
| `analytics.v_net_worth_daily` | Net-worth history | `as_of_date, total_usd, banking_usd, brokerage_usd, crypto_usd` |
| `analytics.v_budgets` | Budgets + period spend | `category, period, limit_amount, spent_amount, remaining` |

Notes:
- Views `SELECT` from the source modules' tables (BankSync, Brokerage/Crypto, Research, NetWorthHistory, Budgets). This is the deliberate read-model layer (plan Complexity Tracking).
- `v_analyst_actions` is market-wide (no user filter) — the exception; everything else is user-scoped.
- Grown deliberately as new query needs appear — not an auto-exposed schema.

## `fs_readonly` role + RLS

- Role `fs_readonly`: `NOLOGIN`-parent granted to a login role used by the read-only connection; `GRANT USAGE ON SCHEMA analytics` + `GRANT SELECT` on the curated views **only**. No grants on base tables, no write privileges anywhere.
- The executor, per query: `SET TRANSACTION READ ONLY; SELECT set_config('app.current_user_id', @caller, true), set_config('statement_timeout', @ms, true);` as the app login, then `SET LOCAL ROLE fs_readonly`, runs the validated SELECT, and `ROLLBACK`s.
- `current_setting` scoping cannot be bypassed by the agent's SQL: `SqlGuard` rejects any non-allowlisted function call (incl. `set_config`/`current_setting`), and migration M002 revokes `EXECUTE` on `pg_catalog.set_config` from PUBLIC, granting it only to the migration login (the app login that runs the executor's setup statement; the revoke is skipped with a warning when that login is not a superuser).

## Entity: `QueryAuditRecord` (table `analytics.query_audit`)

| Field | Type | Notes |
|---|---|---|
| `Id` | Guid (PK) | |
| `UserId` | Guid | caller |
| `Sql` | string | the exact statement submitted (max 8000) |
| `Outcome` | string | `Executed` \| `Rejected` |
| `RejectReason` | string? | when rejected (e.g. "not a single SELECT") |
| `RowCount` | int? | rows returned when executed |
| `DurationMs` | int? | execution time |
| `CreatedAt` | DateTimeOffset | |

Index: `(UserId, CreatedAt)`. Written on the app's normal writable connection (not `fs_readonly`).

## DTOs

- **`AnalyticsQueryResult`**: `{ columns: string[], rows: object[][], rowCount, truncated: bool, sql: string }` — `truncated` true when the row cap clipped results.
- **`QuerySchemaDto`**: `{ views: [{ name, purpose, columns: [{ name, type }] }] }`.
