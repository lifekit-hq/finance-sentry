# Finance Sentry MCP Server

`backend/src/FinanceSentry.Mcp` is an MCP server executable. It does not contain an MCP client. External clients connect to it over either `stdio` or streamable HTTP.

## Transports

| Transport | Selected By | Intended Client |
|---|---|---|
| `stdio` | `MCP_TRANSPORT=stdio` | Claude Desktop, `mcp-probe.sh`, any local process that can spawn `dotnet FinanceSentry.Mcp.dll` |
| `http` | `MCP_TRANSPORT=http` | Containerized clients such as OpenClaw that cannot spawn the MCP process directly |

## Identity

- `stdio` transport uses locally stored MCP OAuth credentials obtained via `dotnet FinanceSentry.Mcp.dll auth login`.
- `http` transport requires per-request authentication via `Authorization: Bearer <mcp access token>` — except the anonymous platform probes `/health`, `/ready` and `/metrics` (exact paths, `McpJwtAuthenticationMiddleware.AnonymousPaths`), which serve nothing user-scoped.
- For HTTP, the MCP server resolves identity from the authenticated request user, not from a boot-time server token.
- `stdio` refresh is automatic through the MCP token endpoint and locally stored refresh token.
- HTTP clients refresh through the MCP token endpoint using dedicated MCP refresh tokens.
- The browser-based `auth login` flow is intended for a host-run `stdio` MCP process. Containerized clients should prefer HTTP MCP.

## Tool Surface

The current runtime surface contains 63 tools. The canonical list is the `AgreedToolSurface` set in `backend/tests/FinanceSentry.Mcp.Tests/ContractTests/ToolNameContractTests.cs`; the table below is a partial, representative view and is not kept row-complete.

| Tool Name | Mode | Domain | Key Inputs | Notes |
|---|---|---|---|---|
| `get_account_summary` | Read | Portfolio | `userId?` | Consolidated banking, crypto, and brokerage balances |
| `list_transactions` | Read | Banking | `userId?`, `accountId?`, `fromDate?`, `toDate?`, `category?`, `page`, `pageSize` | Paginated transaction listing |
| `get_budget_status` | Read | Budgets | `userId?`, `year?`, `month?` | Budget utilization for a period |
| `list_active_alerts` | Read | Alerts | `userId?` | Only unread unresolved alerts |
| `get_portfolio_snapshot` | Read | Portfolio | `userId?` | Unified brokerage + crypto holdings; cash split into banking / brokerage / crypto-venue fiat |
| `list_subscriptions` | Read | Subscriptions | `userId?` | Detected recurring charges |
| `committed_merchants` | Read + Write | Cashflow | `action` (`list`\|`pin`\|`unpin`), `merchant?`, `userId?` | The merchants the user declared committed — rule (d) of the committed-outflow policy |
| `get_sync_health` | Read | Sync | `userId?` | Status across Monobank, TrueLayer, Binance, Revolut X, IBKR |
| `get_crypto_pnl_detail` | Read | Crypto | `userId?` | Per-asset crypto P&L from trade history |
| `get_tax_lots` | Read | Brokerage | `userId?` | Current tax lots / average cost data |
| `get_cashflow_report` | Read | Cashflow | `userId?`, `fromDate?`, `toDate?` | Monthly inflow / outflow / net from the classified money-flow statistics (internal transfers excluded, USD); `TransactionCount` is always 0 — the source query doesn't expose one |
| `get_family_clearing_statement` | Read | Cashflow | `userId?`, `month?`, `months?` | One calendar month's family clearing house: per-`family_support`-counterparty gross received/sent with a presentational net, native per-currency subtotals, and the month's support/received totals; excluded self-routing legs are counted, not dropped silently |
| `get_net_worth_history` | Read | Wealth | `userId?`, `fromDate?`, `toDate?` | Historical net worth snapshots |
| `get_macro_calendar` | Read | Research | `from?`, `to?`, `regions?`, `minImportance?` | Scheduled macro events |
| `get_event_calendar` | Read | Events | `daysAhead?`, `daysBack?`, `kinds?`, `limit?`, `userId?` | Upcoming events (earnings, ex-dividend, derived filing due dates, macro, thesis catalysts) plus the fired events with their outcome (`verdict` / `judged_immaterial` / `silent` / `awaiting` / `not_delivered`) and per-source availability |
| `record_event_verdict` | Write | Events | `eventId`, `verdict`, `notified`, `userId?` | Records the reader's judgement on a fired, alert-sourced companion event; an acknowledged event with no verdict reads as silence; `recorded=false` for a foreign, unknown or analyst-action event or a blank verdict |
| `get_news_for_ticker` | Read | Research | `ticker`, `since?`, `limit` | Recent ticker-specific news |
| `get_quotes` | Read | Research | `tickers` | Current quotes for one or more tickers, including requested/resolved ticker identity and market-session freshness metadata |
| `search_market_news` | Read | Research | `query?`, `tickers?`, `since?`, `limit` | Search ingested market news |
| `search_research_corpus` | Read | Research | `query`, `tickers?`, `thesisId?`, `sourceTypes?`, `from?`, `to?`, `limit?` | Hybrid semantic + lexical search over the stored research corpus; returns cited chunks with scores. No `userId` param — identity-scoped |
| `get_research_context` | Read | Research | `thesisId?` or `ticker`, `question?`, `from?`, `maxChunks?`, `includeSourceTypes?` | Bounded, cited context packet grouped by source type for RAG. No `userId` param — identity-scoped |
| `list_watchlist` | Read | Research | `userId?` | Stored watchlist entries |
| `list_theses` | Read | Research | `userId?` | Stored investment theses |
| `add_to_watchlist` | Write | Research | `ticker`, `exchange?`, `note?`, `userId?` | Adds a watchlist entry |
| `remove_from_watchlist` | Write | Research | `itemId`, `userId?` | Removes a watchlist entry |
| `save_thesis` | Write | Research | `ticker`, `thesisText`, `keyDataPoints`, `catalysts`, `invalidationTriggers`, `id?`, `userId?` | Creates or updates a thesis |
| `delete_thesis` | Write | Research | `id`, `userId?` | Deletes a thesis |

## Research Retrieval Guidance

`search_research_corpus` and `get_research_context` return **non-authoritative research context**: stored news, theses, and decision notes with citations. They are never the source for current balances, holdings, exposure, tax lots, or risk verdicts — those come from the structured portfolio/risk tools. Retrieval tools take no `userId` parameter; visibility is derived from the authenticated MCP identity (global documents plus the caller's own private documents). Embeddings are optional deploy-time configuration (`ResearchRetrieval:Embedding` section, OpenAI-compatible endpoint); with embeddings disabled, ranking degrades to lexical-only. Vectors are stored as plain `real[]` columns — no Postgres extension required; `pgvector` is the documented upgrade path if the corpus outgrows in-app ranking.

## Runtime Model

- The server is assembled in `Program.cs` using `AddMcpServer()`.
- Tools are discovered by assembly scan via `WithToolsFromAssembly(...)`.
- Tool identity comes from the MCP SDK attributes (`[McpServerToolType]` and `[McpServerTool]`), not from a separate local tool interface.
- Each tool is a thin MCP adapter around the existing CQRS handlers in the module projects.
- Development usually runs over `stdio`; production compose runs the same server over HTTP with request-based JWT auth.
- The longer-term OAuth migration plan is documented in `docs/mcp-oauth-roadmap.md`.
