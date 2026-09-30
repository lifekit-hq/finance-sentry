# MCP Tool Contracts: Analytics Query

Two new tools. Both resolve the user from the authenticated MCP identity (`IIdentityResolver`); no default user (FR-004).

## `describe_query_schema` (new)

**Request**: none.
**Response**:
```json
{
  "views": [
    { "name": "analytics.v_transactions", "purpose": "Bank/card transactions",
      "columns": [ {"name":"date","type":"date"}, {"name":"amount","type":"numeric"}, {"name":"category","type":"text"} ] }
  ]
}
```
The agent calls this first to learn exactly what it can query. Lists only the curated views (FR-007) — never raw internal tables.

## `run_analytics_query` (new)

**Request**:
| Param | Type | Required | Notes |
|---|---|---|---|
| `sql` | string | yes | a single read-only `SELECT` over the curated views |

**Response** (success):
```json
{
  "columns": ["category", "total"],
  "rows": [ ["Restaurants", 842.10], ["Groceries", 610.44] ],
  "rowCount": 2,
  "truncated": false,
  "sql": "SELECT category, SUM(amount) AS total FROM analytics.v_transactions WHERE direction='debit' GROUP BY category ORDER BY total DESC"
}
```
**Response** (rejected): `{ "error": "rejected", "reason": "only a single SELECT over the curated views is allowed" }` (a disallowed function call names it: `"function 'set_config' is not allowed — use standard aggregate, window, math, date and text functions only"`)
**Response** (too large): `{ "error": "too_large", "reason": "query exceeded the time/row budget — narrow it (add filters, a date range, or LIMIT)" }`

Rules:
- Read-only enforced by the `fs_readonly` role (FR-002); per-user by RLS (FR-004); single-`SELECT` with allowlisted functions only by the validator (FR-005); bounded by timeout + row cap (FR-006).
- The response ALWAYS echoes the executed `sql` (FR-001) so the agent cites it and Denys can audit.
- **Tool description states**: this is for exploratory/ad-hoc structured questions; authoritative numbers (net worth, risk verdicts, holdings totals) come from their dedicated tools, not this (FR-009).
