# Changelog

All notable changes to Finance Sentry are documented here. The format follows
[Conventional Commits](https://www.conventionalcommits.org/) and versions follow
[Semantic Versioning](https://semver.org/). Entries from v0.12.0 onward are
generated automatically by [release-please](https://github.com/googleapis/release-please).

## [1.1.0](https://github.com/lifekit-hq/finance-sentry/compare/v1.0.0...v1.1.0) (2026-08-08)


### Features

* **021:** market regime scanner — VIX + FRED curve, orthogonal axes, get_market_regime(), 019 coupling ([#386](https://github.com/lifekit-hq/finance-sentry/issues/386)) ([e795895](https://github.com/lifekit-hq/finance-sentry/commit/e795895560439ba85c95074499de4104d045be04))
* **024:** data retention & verified off-host backups ([#367](https://github.com/lifekit-hq/finance-sentry/issues/367)) ([84922b1](https://github.com/lifekit-hq/finance-sentry/commit/84922b176ea2b0a06518cc9189f9a76a2492873c))
* **025:** edge gateway — YARP reverse proxy (additive, no cutover) ([#383](https://github.com/lifekit-hq/finance-sentry/issues/383)) ([64b2dee](https://github.com/lifekit-hq/finance-sentry/commit/64b2dee1a9b55d090ae6a7515843ab849b51e4c1))
* **039:** IPS/Risk boundary cleanup — single home per policy concept ([#382](https://github.com/lifekit-hq/finance-sentry/issues/382)) ([190fb31](https://github.com/lifekit-hq/finance-sentry/commit/190fb3144fb29ea9416bd2685210c781c7979732))
* **040:** in-app finance agent — browser Ledger (US2/US3) over MCP tool surface ([#389](https://github.com/lifekit-hq/finance-sentry/issues/389)) ([dc97790](https://github.com/lifekit-hq/finance-sentry/commit/dc977901ed09ec939f6198d1f8a11dc0a406c89e))
* **040:** OpenClaw-backed Ledger brain + floating chat widget ([#390](https://github.com/lifekit-hq/finance-sentry/issues/390)) ([2590eb6](https://github.com/lifekit-hq/finance-sentry/commit/2590eb64f6f5e4e31c216a7341d5686df885a4fa))
* **040:** US1 — Ledger persona as code (core + OpenClaw/browser adapters) ([#388](https://github.com/lifekit-hq/finance-sentry/issues/388)) ([889a06a](https://github.com/lifekit-hq/finance-sentry/commit/889a06a01307ad83b825c4040950b62112a3b88b))
* **accounts:** merge Investments into Accounts as tabs; drop duplicate donut ([#379](https://github.com/lifekit-hq/finance-sentry/issues/379)) ([915c34d](https://github.com/lifekit-hq/finance-sentry/commit/915c34d6f5e8e363f6dd3054e0da24a315910239))
* **categorization:** seed common IE merchants for the TrueLayer no-MCC fallback ([#365](https://github.com/lifekit-hq/finance-sentry/issues/365)) ([d8173a5](https://github.com/lifekit-hq/finance-sentry/commit/d8173a567952ee86b3f67c17c5ec2d71999df402))
* **dashboard:** restore income analytics now that income is classified ([#376](https://github.com/lifekit-hq/finance-sentry/issues/376)) ([fe17121](https://github.com/lifekit-hq/finance-sentry/commit/fe17121a27d94fc731276a84665b585198d95c4e))
* **dashboard:** time-based analytics — stacked net worth, cash-flow & savings bars ([#371](https://github.com/lifekit-hq/finance-sentry/issues/371)) ([03c0b46](https://github.com/lifekit-hq/finance-sentry/commit/03c0b4642ed7c1d47c9f1d0a5d44116af2ddb9c4))
* **ia:** split Accounts vs Investments; drop duplicated allocation view ([#370](https://github.com/lifekit-hq/finance-sentry/issues/370)) ([cd23b77](https://github.com/lifekit-hq/finance-sentry/commit/cd23b77fc2798f9e38f2662d33e8981171522f05))
* **ibkr:** surface uninvested cash from the IBKR ledger ([#375](https://github.com/lifekit-hq/finance-sentry/issues/375)) ([d278bf0](https://github.com/lifekit-hq/finance-sentry/commit/d278bf08438f64cea15e69944e6a01e72b44fd87))
* **investments:** apply net-worth hero to the Investments header ([#378](https://github.com/lifekit-hq/finance-sentry/issues/378)) ([61a8e03](https://github.com/lifekit-hq/finance-sentry/commit/61a8e037945b0b9f382513ff3b3751f0d52ec3f9))
* **monobank:** group currency sub-accounts under their physical card ([#366](https://github.com/lifekit-hq/finance-sentry/issues/366)) ([96073a7](https://github.com/lifekit-hq/finance-sentry/commit/96073a748a5ffef2162514658027e10d19d1e198))
* **ui:** internal scroll + sticky header for data pages ([#380](https://github.com/lifekit-hq/finance-sentry/issues/380)) ([2b99047](https://github.com/lifekit-hq/finance-sentry/commit/2b9904705364fe3dc423e6ed457aa30294e89368))
* **ui:** subtle theme-aware scrollbars ([#381](https://github.com/lifekit-hq/finance-sentry/issues/381)) ([0f98962](https://github.com/lifekit-hq/finance-sentry/commit/0f9896234d89643eba7c94820ac6b526c49a9c62))
* **wealth:** surface stale bank connections; stop them faking net-worth moves ([#363](https://github.com/lifekit-hq/finance-sentry/issues/363)) ([b516653](https://github.com/lifekit-hq/finance-sentry/commit/b5166533fa73179fd05c13317f81968972a722ea))


### Bug Fixes

* **024:** retention module governs its own run-record tables ([#368](https://github.com/lifekit-hq/finance-sentry/issues/368)) ([8a3edb6](https://github.com/lifekit-hq/finance-sentry/commit/8a3edb6799f6ed75650bc25447ae56f3974c7d3e))
* **025:** allow gateway host in dev-server so SPA routes through the gateway ([#384](https://github.com/lifekit-hq/finance-sentry/issues/384)) ([a0ac3b9](https://github.com/lifekit-hq/finance-sentry/commit/a0ac3b9f2417e2e1036b3d45778b39f81ce1d743))
* **039:** register cross-module ports in the MCP host via shared FinanceSentry.Integration lib ([#387](https://github.com/lifekit-hq/finance-sentry/issues/387)) ([a260d0b](https://github.com/lifekit-hq/finance-sentry/commit/a260d0b8cac8066db79916383ffeb78f419779bf))
* **accounts:** USD-correct monthly outflow; rebuild net-worth hero ([#377](https://github.com/lifekit-hq/finance-sentry/issues/377)) ([713734d](https://github.com/lifekit-hq/finance-sentry/commit/713734dc4515f950707e69300a0067c55228a33e))
* **bank-sync,research:** map Monobank 403 to token-invalid; strip MarketBeat promo from firm names (M011 data repair) ([#361](https://github.com/lifekit-hq/finance-sentry/issues/361)) ([e132154](https://github.com/lifekit-hq/finance-sentry/commit/e1321546973fcc989a5ffaaf6750ba6a0bcb5741))
* **categorization:** classify external "Payment from" credits as income, not transfer ([#374](https://github.com/lifekit-hq/finance-sentry/issues/374)) ([68e5e80](https://github.com/lifekit-hq/finance-sentry/commit/68e5e80c9937dde79f8c74c5ba1116b7e3d9ca20))
* **connect:** reset error state on navigation, preserve form on failed submit, fix error-code resolution ([#362](https://github.com/lifekit-hq/finance-sentry/issues/362)) ([d143076](https://github.com/lifekit-hq/finance-sentry/commit/d143076670da6eab0df021adde74c8d62221fcf9))
* **currency:** convert to USD before every cross-currency aggregation ([#357](https://github.com/lifekit-hq/finance-sentry/issues/357)) ([f1bb92b](https://github.com/lifekit-hq/finance-sentry/commit/f1bb92b2b5d53655582ec42784dba35424434b5e))
* dashboard KPI layout-shift on range change; harden deploy against api race ([#373](https://github.com/lifekit-hq/finance-sentry/issues/373)) ([662cc10](https://github.com/lifekit-hq/finance-sentry/commit/662cc1021bdf0e7d72e79fb6b41f3887ba9de8d7))
* **holdings:** show only provider-backed P&L; fix Monobank card indent ([#369](https://github.com/lifekit-hq/finance-sentry/issues/369)) ([6385f83](https://github.com/lifekit-hq/finance-sentry/commit/6385f833e610f61a28d55f64de2fae9f70d2d2fc))
* **subscriptions:** update list and summary in place on dismiss/restore ([#359](https://github.com/lifekit-hq/finance-sentry/issues/359)) ([5b992df](https://github.com/lifekit-hq/finance-sentry/commit/5b992df1d0fd918166588c4efbfd211be163eead))
* **tests:** add AmountUsd arg to GlobalTransactionDto test builders ([38ab03f](https://github.com/lifekit-hq/finance-sentry/commit/38ab03f6953b08edc3ef310411624614129fb59e))
* **ui:** make cmn-dialog viewport-safe below 512px ([#360](https://github.com/lifekit-hq/finance-sentry/issues/360)) ([443a188](https://github.com/lifekit-hq/finance-sentry/commit/443a18826db2aa0e875f56a3cd4d34fd0eef4d73))
* **ui:** premium theme + honest dashboard; drop charts built on absent income ([#372](https://github.com/lifekit-hq/finance-sentry/issues/372)) ([93d02da](https://github.com/lifekit-hq/finance-sentry/commit/93d02dacf9efccf30ae1608e03d176aab6805667))


### Documentation

* **011:** sign off T067 — 4 QA-sweep bugs fixed and re-verified ([#364](https://github.com/lifekit-hq/finance-sentry/issues/364)) ([56cd192](https://github.com/lifekit-hq/finance-sentry/commit/56cd192dd28cf4fdb4568ce9830085c5ed5cb694))
* **025:** record fast-fail as known limitation, deferred to 027-k8s ([#385](https://github.com/lifekit-hq/finance-sentry/issues/385)) ([48ae4e5](https://github.com/lifekit-hq/finance-sentry/commit/48ae4e5d062f5fa9ea214783284878db93e8b014))
* **qa:** record 2026-08-06 QA sweep results for 008/014/030/036, annotate 011 T067 ([#358](https://github.com/lifekit-hq/finance-sentry/issues/358)) ([09afee3](https://github.com/lifekit-hq/finance-sentry/commit/09afee32e1108e7327f3d1720597d36351f61e2e))

## 1.0.0 (2026-08-06)

First stable release. Finance Sentry is a personal finance aggregation platform —
an ASP.NET Core (.NET 10) modular monolith + Angular 21 SPA — that consolidates
banking, brokerage, and crypto accounts into one net-worth, spending, and research
view. All golden-path flows (login, accounts, dashboard, transactions, holdings,
budgets, subscriptions) are verified end-to-end against live data.

### Features

- **Research suite (companion data layer)**: analyst actions, valuation snapshots,
  thesis-source news (030); structured analyst data via Finnhub recommendation
  trends, retiring the Yahoo scraper (037); retrieval + RAG context layer with
  `search_research_corpus` / `get_research_context` MCP tools (036); opportunity
  scan job with machine nomination from market structure (019); MCP tool-surface
  refinement (035); guarded read-only analytics query tool (033)
- **Companion notifications**: notification modes + event-driven push (031)
- **Observability stack**: OpenTelemetry, Loki, Prometheus, Grafana dashboards,
  Hangfire-on-Postgres, job-failure Telegram alerts (023)
- **Brokerage**: IBKR holdings via tier-1 Portal session + OAuth
- **FX**: live exchange rates with daily refresh and offline fallback
- **Installments**: dedicated section with smarter detection + management
- **UI library**: new `@dsdevq-common/ui` composites — `cmn-page-header`,
  `cmn-page-container`, `cmn-tab-group`, `cmn-empty-state`, `cmn-disclosure-row`,
  `cmn-editable-field`, `cmn-list-item-row`, `cmn-select`; provider logos,
  per-asset holding icons, app version in the sidebar footer

### Bug Fixes

- **Categorization**: classify Monobank savings-jar top-ups as transfers, not
  government spend; categorize TrueLayer transactions from description text;
  case-insensitive transfer bucketing
- **TrueLayer**: self-healing reconnect, stale-sync reaper, dedup crash fix,
  rotated-refresh-token persistence, inline history-fetch + pre-expiry reminder
- **Reliability**: stop silent cron-failure loops (TrueLayer, TrendForce, Yahoo);
  treat provider 429s as transient (no false SyncFailure alerts)
- **Holdings/subscriptions**: drop zero-quantity and sold-out positions; exclude
  installments and canonicalize noisy merchant brands; USD-normalized monthly cost
- **Frontend**: green lint-build-test enforced pre-commit; alerts page no longer
  crashes on unmapped alert types; chart tooltips + URL-persisted dashboard range
- **Portfolio/quotes**: quote data-quality fixes; missing migration Designer for
  `M007_QuoteSessionMetadata`

## 0.11.0 (2026-07-09)

Baseline release — consolidation of everything built to date under a single
application version (previously the frontend and backend were versioned
independently, last tagged `frontend-v0.4.0` / backend `0.11.0`).

### Highlights

- Multi-provider aggregation: Plaid, Monobank, TrueLayer, Binance, IBKR
- Auth with email/password + Google OAuth, JWT + httpOnly refresh cookie
- Dashboard, transactions, budgets, alerts, subscription detection
- Net worth history snapshots
- Research suite: investment theses, thesis monitor, thesis track record, market structure radar, opportunity scanner, risk rules
- Read-only MCP server (`FinanceSentry.Mcp`) exposing financial data to MCP clients
- Full Docker stack + CI + automated VPS deployment
