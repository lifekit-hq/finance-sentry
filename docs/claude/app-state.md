# Finance Sentry — App State & Key Files

> Reference document (not auto-loaded). Current state of what is built and running.
> Update the relevant block when a feature lands; keep each feature to ONE paragraph.

## Key Files

```
backend/
  src/
    FinanceSentry.API/
      Program.cs                          # DI registrations, middleware pipeline
    FinanceSentry.Modules.BankSync/
      API/
        Controllers/                      # REST controllers
        Middleware/
          JwtAuthenticationMiddleware.cs  # JWT validation; exempt: /health, /api/v1/health, /swagger, /api/webhook, /hangfire
      Application/                        # CQRS commands/queries (MediatR)
      Domain/                             # Entities, interfaces, repositories
      Infrastructure/                     # EF Core, provider HTTP clients, encryption

frontend/
  src/app/
    app.routes.ts                         # / → /accounts (lazy bank-sync module), /dashboard, /login, /register
    app.config.ts                         # provideRouter + provideHttpClient(withInterceptors([authInterceptor])) + provideErrorHandler() + provideErrorMessages()
    core/
      providers/                          # provide*() factories returning EnvironmentProviders (one per concern)
      errors/error-messages.registry.ts   # app-owned Record<errorCode, message> consumed by @lifekit-hq/ui's ErrorMessageService
      handlers/http-error.handler.ts      # global ErrorHandler → toasts
    shared/
      enums/app-route.enum.ts             # route literals
      utils/                               # cross-module pure helpers (e.g. getRelativeTime)
    modules/auth/
      store/                              # auth.state/computed/methods/effects/store.ts + specs
      services/auth.service.ts            # HTTP-only (no state)
      interceptors/auth.interceptor.ts    # reads AuthStore.token(), refreshes on 401
      guards/                             # authGuard / guestGuard — signal-based
      pages/login · register              # declarative components bound to AuthStore signals
      validators/password-match.validator.ts
    modules/bank-sync/
      store/dashboard                     # DashboardStore (component-scoped via providers) — RxJS timer refresh
      store/accounts                      # AccountsStore — list + disconnect
      services/bank-sync.service.ts       # HTTP-only
      pages/                              # accounts-list, connect-account, transaction-list, dashboard

docker/
  docker-compose.dev.yml                  # Full stack: postgres + api + frontend
  Dockerfile                              # Multi-stage backend build (includes NuGet.Config copy)
  Dockerfile.frontend                     # Node 22 Alpine, ng serve

.specify/
  memory/constitution.md                  # Project governance (v1.2.0)
  specs/001-bank-account-sync/            # Feature spec, plan, tasks for bank sync
```

---

## Current App State

**What works:**
- Full Docker stack runs and all three containers are healthy
- API health check: `GET /api/v1/health` → `{"status":"healthy"}`
- Auth: login/register/Google sign-in. Access token is held in-memory by `AuthStore` (no localStorage); refresh token is an httpOnly cookie. `authInterceptor` attaches `Bearer` from the store and refreshes on 401. Silent refresh on app init hydrates the session from the cookie. `authGuard`/`guestGuard` protect routes.
- State: `AuthStore`, `DashboardStore`, `AccountsStore` built as NgRx SignalStores with feature-file split (state/computed/methods/effects/store)
- Vitest unit tests covering the signal stores (run with `npx ng test --watch=false`)
- All bank-sync pages render (accounts list, connect, transactions, dashboard)
- Backend: accounts, transactions, sync, webhook, dashboard endpoints all implemented
- Data retention & backups (024): `FinanceSentry.Modules.Retention` (schema `retention`). A compiled `RetentionPolicyRegistry` gives every table a purge/downsample/keep decision (reflection coverage-guard + keep-forever whitelist tests). Nightly `retention-purge` batch-deletes out-of-policy rows idempotently; nightly `db-backup` does `pg_dump → age-encrypt → Cloudflare R2`; weekly `db-restore-verify` restores into an isolated scratch DB and marks the artifact Verified. US3 `retention-downsample` (weekly compaction) is gated off (`Retention:Downsample:Enabled`). Ops verbs: `dotnet FinanceSentry.API.dll retention-purge [--dry-run] | db-backup | db-restore-verify`. R2/age secrets in `.env.sops` (`BACKUP_*`); blank = jobs no-op. Grafana dashboard `retention-backups`.

**Frontend state sweep — DONE.** All page-level state now lives in SignalStores under `modules/bank-sync/store/` (`accounts`, `connect`, `dashboard`, `sync-status`, `transactions`, `transaction-ledger`). The connect flow moved to `components/connect-modal/` backed by `ConnectStore` (error mapping via `ERROR_MESSAGES_REGISTRY`); transactions use `TransactionsStore`; `sync-status.component.ts` polling folded into `SyncStatusStore` (`rxMethod` timer, no `setInterval`).

**In-app finance agent / browser Ledger (040) — US2+US3 DONE.** Ledger now lives in FS as a server-side Claude tool-use loop. New `FinanceSentry.Modules.Agent` (schema `agent`, migration **M001** — `agent_conversations` + `agent_messages`, applied via `MigrateAllModules`): `ILlmClient`/`AnthropicLlmClient` (Anthropic Messages API over `IHttpClientFactory` + `System.Text.Json` — streaming SSE + tool-use; keyless ⇒ typed `AgentNotConfigured`, no HTTP), `McpToolBridge` (reflects **every** `[McpServerTool]` in `FinanceSentry.Mcp` → Anthropic tool schema via `JsonSchemaExporter`, verbatim name/description/input_schema with `userId` stripped; dispatches each `tool_use` in the authenticated request scope so tools resolve the caller's id — FR-008), `PersonaComposer` (composes `agent/ledger/persona.core.md` + `adapters/browser.md` + `user.md`; files shipped into the API publish output), `AgentConversationService` (the bounded loop; `MaxToolIterations`/`HistoryTurnBudget` caps). Endpoint `POST /api/v1/agent/chat` streams `text/event-stream` (events conversation/text/tool/error/done) + `GET/DELETE /agent/conversations` — behind JWT (not exempt); keyless ⇒ single `agent_not_configured`. Frontend: `cmn-chat-message`/`cmn-chat-input` in `@lifekit-hq/ui`; `modules/agent/` with `AgentChatStore` (5-file split, `rxMethod` consumes SSE), fetch-based `AgentService`, declarative `/ledger` page (lazy, `authGuard`, nav + palette entry). Config: `ANTHROPIC_API_KEY` → `Agent__Anthropic__ApiKey` (server-only; blank = chat disabled, rest of app unaffected). **OpenClaw Ledger unchanged** (FR-015) — no MCP tool definitions, `persona.core.md`, or `adapters/openclaw.md` touched; both runtimes compose the same core (parity test guards the split). Retention: both agent tables registered (Keep). Live chat needs the Anthropic key; all key-independent paths (keyless, auth, list/delete, tool-bridge, loop, persona/parity, SSE shape) tested green.

**IPS/Risk boundary (039) — DONE.** Each policy concept has exactly one home: **target allocation → IPS** (`InvestmentPolicyStatement.AllocationTargets` + `RebalancingRule`, intent); **single-position cap → Risk** (`RiskRuleSet.MaxPositionWeightPct`, enforced fraction 0–1). Duplicate fields dropped (`IPS.MaxSinglePositionPct`, `RiskRuleSet.AllocationTargets`). Cross-module reads go through read-only ports (`IAllocationPolicySource` in Risk.Domain, `IPositionCapSource` in Research.Domain) whose adapters live in `FinanceSentry.API/Integration/` (the two modules don't reference each other) and delegate to the other module's `GetIpsQuery`/`GetRiskRuleSetQuery`; wired via `AddCrossModulePorts()`. `RiskEvaluationService.Evaluate` takes allocation targets as a param (stays pure); callers (`RiskCheckJob`, `CheckRiskRulesQuery`) fetch via the port. Migrations Risk **M002** (reconcile allocation→IPS, drop `allocation_targets_json`) and Research **M012** (reconcile cap→Risk stricter-wins + unit-normalize, drop `MaxSinglePositionPct`) are order-independent (each reconciles the concept it drops, writing to the other schema's retained column). save_ips/save_risk_rules + `PUT/GET /risk/rules` dropped the moved fields; **agent-config (Ledger persona) owner must update prompts** — moved fields: `save_ips.maxSinglePositionPct`→`save_risk_rules.maxPositionWeightPct`, `save_risk_rules.allocationTargets`→`save_ips.allocationTargets`.

**Ledger persona-as-code (040, US1) — DONE (feature 040 fully complete; T028 QA passed against prod 2026-08-12).** The Ledger finance agent's persona is now versioned in the repo at `agent/ledger/` as the canonical source of truth: `persona.core.md` (runtime-agnostic identity/expertise/operating-laws/tool-philosophy, no runtime mechanics, no hard-coded policy values), `user.md` (Denys profile), and `adapters/{openclaw,browser}.md`. A runtime's effective persona = **core + exactly one adapter**; `core + adapters/openclaw.md` reproduces the live OpenClaw Ledger, `core + adapters/browser.md` is the target for the in-app browser agent (040 US2, runtime deferred to plan). "FS is core, agent is thin" — the core reads live policy (IPS/risk/allocation) via tools, never hard-codes it. OpenClaw + browser Ledger **coexist**, sharing the core. Spec: `specs/040-in-app-finance-agent/`.

**Edge gateway (025) — DONE (production cutover 2026-08-12).** New `FinanceSentry.Gateway` ASP.NET Core host (YARP `Yarp.ReverseProxy`) is the single reverse-proxy front door for frontend + API + MCP. Routing/clusters are declarative in `backend/src/FinanceSentry.Gateway/appsettings.json` (`ReverseProxy` section): path-based — `/api/v1/auth/**` + `/api/webhook/**` (rate-limited) → `/api/**` → `/hangfire/**` → api:5000; `/mcp/**` (prefix-stripped) → mcp:5100; `/{**catch-all}` → frontend:4200. Per-client (real-IP) fixed-window rate limits (`auth` 10/min, `webhook` 60/min; 429 on abuse; config-tunable `Gateway:RateLimits:*`) via `GatewayRateLimitPolicies`. Active+passive YARP health checks (api probes `/api/v1/health` @5s; steady-state fast 503 on outage; multi-destination-ready for replicas). `UseForwardedHeaders` on both gateway and API so real client IP/scheme reach backend logs (FR-006) — **API version bumped 1.1.0→1.2.0**. TLS is config-gated ACME (`LettuceEncrypt`): active only when `LettuceEncrypt:DomainNames` set + `AcceptTermsOfService=true`; else plain HTTP (dev / Tailscale-terminated prod). OTel→Prometheus `/metrics` + new `finance-sentry-gateway` scrape job. Gateway runs in dev (`:8080`, direct ports kept for dev parity FR-008) + prod compose (`docker/Dockerfile.gateway`). Tests: `FinanceSentry.Gateway.Tests` (6 config-invariant xUnit, green). **Production cutover DONE 2026-08-12**: Tailscale Serve `:4200` and `:5001` both proxy loopback `:8080` (gateway path-routes SPA + API, so all old URLs still work); frontend/api host ports removed from prod compose; `deploy.sh` health-checks via the gateway. TLS stays Tailscale-terminated (no ACME). Rollback documented in `specs/025-edge-gateway/quickstart.md`.

**Hygiene sentinels (044) — DONE.** Four deterministic daily Hangfire jobs in `FinanceSentry.Modules.BankSync/Infrastructure/Jobs/`, all emitting through Alerts (012) — no LLM on the tick path, no new external feed. `PriceHikeDetectionJob` reads `detected_subscriptions` (014) through the Core port `ISubscriptionHygieneSummaryReader` and measures against `HikeBaseline` (`PreviousAmount ?? AverageAmount`) — detection records the displaced cluster's price on a repricing (Subscriptions **M006**), because the raised charge sits in the average that would otherwise dilute it. `DuplicateChargeDetectionJob` groups `(account, normalized merchant, amount)` and skips the unnameable-merchant group. `CategorySpikeDetectionJob` (supersedes the deleted `UnusualSpendDetectionJob`, whose Hangfire schedule is withdrawn by name) compares month-to-date USD spend against total spend over the window divided by the months the user was observed for, capped at 6 — a month inside the observed span with no spend in the category is a real zero, a month before the user's first transaction is no data. `FxSpreadDetectionJob` pairs cross-currency legs via `ITransferDetectionService`, measures **settled legs only** (a hold's amount is provisional, and an FX hold that settles at a different amount is never retired by `PendingReconciler` — it lingered active and alerted a second time under its own debit id), and stands down when `CurrencyConverter.AreRatesFresh` is false — a spread measured against the offline seed table accuses honest conversions. Thresholds bind from the `HygieneSentinels` appsettings section into `HygieneSentinelsOptions`. All four alert types carry an active-alert gate + silence window in `AlertGeneratorService.EmitAsync`; a reflection test asserts every live `AlertType` declares one. Spec: `specs/044-hygiene-sentinels/`.

**Known broken:** nothing currently. (The stale Playwright specs under `frontend/tests/integration/bank-sync/` were deleted 2026-04-24, commit `5e0e886`.)

---

## Open Follow-ups from speckit.analyze (2026-04-11)

From the cross-artifact analysis of `specs/001-bank-account-sync/`:

- `spec.md`: Resolve `[NEEDS CLARIFICATION]` webhook note — record formal `[DECISION]`
- `plan.md`: Update constitution reference from v1.0.0 → v1.1.1; add versioning compliance row
- `tasks.md`: Add IBankProvider interface task (C2), Phase 4/5 contract test tasks (C3), migration task for `archived_reason` column (H4), re-auth frontend flow task (H5)

---
