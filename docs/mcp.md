# Finance Sentry MCP Server

`backend/src/FinanceSentry.Mcp` is an MCP server executable. It does not contain an MCP client. External clients connect to it over either `stdio` or streamable HTTP.

## Transports

| Transport | Selected By | Intended Client |
|---|---|---|
| `stdio` | `MCP_TRANSPORT=stdio` | Claude Desktop, `mcp-probe.sh`, any local process that can spawn `dotnet FinanceSentry.Mcp.dll` |
| `http` | `MCP_TRANSPORT=http` | Containerized clients such as OpenClaw that cannot spawn the MCP process directly |

## Identity

- `stdio` transport uses locally stored MCP OAuth credentials obtained via `dotnet FinanceSentry.Mcp.dll auth login`.
- `http` transport requires per-request authentication via `Authorization: Bearer <mcp access token>` (stock JwtBearer, `aud=mcp`, default-deny fallback policy; the MCP endpoint additionally requires `mcp.connect` for a personal token or `mcp.service` for a service token, each independent of the other; the token's account must exist and not be locked out, and a service token must still be active and its account still hold `mcp.service`) — except the platform probes `/health`, `/ready` and `/metrics`, mapped `[AllowAnonymous]` and pinned by `McpAuthenticationPipelineTests`, which serve nothing user-scoped.
- For HTTP, the MCP server resolves identity from the authenticated request user, not from a boot-time server token.
- Every tool acts only for the authenticated user: no tool takes a `userId` argument, and a regression contract test (`ToolCallerScopeContractTests`) guards that.
- `stdio` refresh is automatic through the MCP token endpoint and locally stored refresh token.
- HTTP clients refresh through the MCP token endpoint using dedicated MCP refresh tokens.
- The browser-based `auth login` flow is intended for a host-run `stdio` MCP process. Containerized clients should prefer HTTP MCP.

## Tool Surface

The current runtime surface contains 68 tools. The canonical list is the `AgreedToolSurface` set in `backend/tests/FinanceSentry.Mcp.Tests/ContractTests/ToolNameContractTests.cs`; the table below is a partial, representative view and is not kept row-complete.

| Tool Name | Mode | Domain | Key Inputs | Notes |
|---|---|---|---|---|
| `get_account_summary` | Read | Portfolio | — | Consolidated banking, crypto, and brokerage balances |
| `list_transactions` | Read | Banking | `accountId?`, `fromDate?`, `toDate?`, `category?`, `page`, `pageSize` | Paginated transaction listing |
| `get_budget_status` | Read | Budgets | `year?`, `month?` | Budget utilization and month-end pace for a period |
| `list_active_alerts` | Read | Alerts | — | Only unread unresolved alerts |
| `get_portfolio_snapshot` | Read | Portfolio | — | Unified brokerage + crypto holdings; cash split into banking / brokerage / crypto-venue fiat |
| `list_subscriptions` | Read | Subscriptions | — | Detected recurring charges |
| `committed_merchants` | Read + Write | Cashflow | `action` (`list`\|`pin`\|`unpin`), `merchant?` | The merchants the user declared committed — rule (d) of the committed-outflow policy |
| `get_sync_health` | Read | Sync | — | Status across Monobank, TrueLayer, Binance, Revolut X, IBKR |
| `get_crypto_pnl_detail` | Read | Crypto | — | Per-asset crypto P&L from trade history |
| `get_tax_lots` | Read | Brokerage | — | Current IBKR tax lots / average cost data (Inzhur holdings are not tax lots) |
| `get_cashflow_report` | Read | Cashflow | `fromDate?`, `toDate?` | Monthly inflow / outflow / net from the classified money-flow statistics (internal transfers excluded, USD); `TransactionCount` is always 0 — the source query doesn't expose one |
| `get_family_clearing_statement` | Read | Cashflow | `month?`, `months?` | One calendar month's family clearing house: per-`family_support`-counterparty gross received/sent with a presentational net, native per-currency subtotals, and the month's support/received totals; excluded self-routing legs are counted, not dropped silently |
| `get_net_worth_history` | Read | Wealth | `fromDate?`, `toDate?` | Historical net worth snapshots, each with `cashTotal` / `brokerageInvested` / `cryptoInvested` (null = no split) |
| `get_policy_review` | Read | Research | — | Scheduled IPS review state: recorded cadence, last reviewed, next due, due/missed and days overdue, plus the latest review's proposal (per-sleeve drift against the IPS bands, Trim / Add / Review adjustments with rationale). Recommend-only. Also reports `riskMeasuredAt`, `drawdownToleranceSet` and `riskRemeasurementDue` (risk re-measurement rides the same cadence) |
| `get_ips_diff` | Read | Research | `fromVersion?`, `toVersion?` | Field-by-field diff between two IPS versions (default: current against the one before it), each change with its before / after JSON value. Null when there is no earlier version |
| `record_risk_remeasurement` | Write | Research | `riskTolerance`, `riskCapacity`, `maxDrawdownTolerancePct` | Records the owner's re-measured tolerance (1-5), capacity (1-5) and maximum tolerated drawdown (percent) as a NEW IPS version; every other field carries forward. The drawdown is enforced by the risk layer (`MaxDrawdown` violation, subject `BOOK`). Returns the new version and the diff against the superseded one |
| `get_macro_calendar` | Read | Research | `from?`, `to?`, `regions?`, `minImportance?` | Scheduled macro events |
| `get_event_calendar` | Read | Events | `daysAhead?`, `daysBack?`, `kinds?`, `limit?` | Upcoming events (earnings, ex-dividend, derived filing due dates, macro, thesis catalysts) plus the fired events with their outcome (`verdict` / `judged_immaterial` / `silent` / `awaiting` / `not_delivered`) and per-source availability |
| `get_pending_companion_events` | Read | Events | `limit?`, `includeHeldForDigest?`, `heldOverrideReason?` | Undelivered companion events (`id`, `kind`, `subject`, `severity`, `summary`, `referenceId`, `disposition`, `occurredAt`) plus `appUrl`: an absolute link to the app page the event is about, built from `Companion:PublicBaseUrl` (deploys bind `FRONTEND_BASE_URL`) and the event's `appPath` (#466). Additive: the key is absent, never null or relative, when the event has no target page or no base URL is configured |
| `record_event_verdict` | Write | Events | `eventId`, `verdict`, `notified` | Records the reader's judgement on a fired companion event, alert-sourced or not (e.g. `AnalystAction`); an acknowledged event with no verdict reads as silence; `recorded=false` for a foreign, unknown event or a blank verdict |
| `get_daily_event_outcomes` | Read | Events | `date?` | Accountability view for one UTC day: every companion event that fired, of any kind, with fired/judged/sent/withheld counts and the per-item outcome; unlike `get_event_calendar` it is not limited to the five alert-backed event types |
| `get_news_for_ticker` | Read | Research | `ticker`, `since?`, `limit` | Recent ticker-specific news |
| `get_quotes` | Read | Research | `tickers` | Current quotes for one or more tickers, including requested/resolved ticker identity and market-session freshness metadata |
| `search_market_news` | Read | Research | `query?`, `tickers?`, `since?`, `limit` | Search ingested market news |
| `search_research_corpus` | Read | Research | `query`, `tickers?`, `thesisId?`, `sourceTypes?`, `from?`, `to?`, `limit?` | Hybrid semantic + lexical search over the stored research corpus; returns cited chunks with scores |
| `get_research_context` | Read | Research | `thesisId?` or `ticker`, `question?`, `from?`, `maxChunks?`, `includeSourceTypes?` | Bounded, cited context packet grouped by source type for RAG |
| `list_watchlist` | Read | Research | — | Stored watchlist entries |
| `list_theses` | Read | Research | — | Stored investment theses |
| `add_to_watchlist` | Write | Research | `ticker`, `exchange?`, `note?` | Adds a watchlist entry |
| `remove_from_watchlist` | Write | Research | `itemId` | Removes a watchlist entry |
| `save_thesis` | Write | Research | `ticker`, `thesisText`, `keyDataPoints`, `catalysts`, `invalidationTriggers`, `id?` | Creates or updates a thesis |
| `delete_thesis` | Write | Research | `id` | Deletes a thesis |

## Research Retrieval Guidance

`search_research_corpus` and `get_research_context` return **non-authoritative research context**: stored news, theses, and decision notes with citations. They are never the source for current balances, holdings, exposure, tax lots, or risk verdicts — those come from the structured portfolio/risk tools. Retrieval visibility is derived from the authenticated MCP identity (global documents plus the caller's own private documents). Embeddings are optional deploy-time configuration (`ResearchRetrieval:Embedding` section, OpenAI-compatible endpoint); with embeddings disabled, ranking degrades to lexical-only. Vectors are stored as plain `real[]` columns — no Postgres extension required; `pgvector` is the documented upgrade path if the corpus outgrows in-app ranking.

## Runtime Model

- The server is assembled in `Program.cs` using `AddMcpServer()`.
- Tools are discovered by assembly scan via `WithToolsFromAssembly(...)`.
- Tool identity comes from the MCP SDK attributes (`[McpServerToolType]` and `[McpServerTool]`), not from a separate local tool interface.
- Each tool is a thin MCP adapter around the existing CQRS handlers in the module projects.
- Development usually runs over `stdio`; production compose runs the same server over HTTP with request-based JWT auth.
- The longer-term OAuth migration plan is documented in `docs/mcp-oauth-roadmap.md`.
