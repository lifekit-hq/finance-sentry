# Finance Sentry — Agent Harness

## Quick-start

```bash
# Restore + build (required on a fresh clone or after switching branches)
cd backend && dotnet restore FinanceSentry.sln
dotnet build FinanceSentry.sln --no-restore -c Release

# Run tests — no filter, no -m flags (`backend/Directory.Build.rsp` pins one build node).
# `scripts/test-backend.sh` runs the same in a 2 GB SDK container (docker/docker-compose.test.yml),
# the production host's per-build limit. CI runs the full solution too; container-backed tests
# report themselves as Skipped where no Docker daemon is reachable.
dotnet test FinanceSentry.sln --no-build -c Release
```

### Frontend (lint, build, e2e — required when touching app-surface UI)

```bash
# @lifekit-hq/* installs from GitHub Packages: export NODE_AUTH_TOKEN=$(gh auth token)
# (token needs read:packages). See frontend/.npmrc.
cd frontend
npm ci
npm run lint
npm run build
npx playwright test --reporter=json
```

Commit hook rule: `.husky/pre-commit` (wired by `frontend`'s `prepare` script) runs lint-staged
(eslint --fix + prettier on staged frontend files) only when `frontend/src/**` or `frontend/projects/**` is staged;
the full lint and format check run in CI. It never runs `npm ci` — it assumes `frontend/node_modules` exists for
frontend commits — and is not something to bypass.

CI-only load gate: Frontend CI also builds the production image (`docker/Dockerfile.frontend.prod` precompresses
every text asset to `.br`/`.gz`, served by `brotli_static`/`gzip_static` in `docker/nginx.frontend.conf`) and runs
`frontend/scripts/measure-lcp.mjs` against it: cold `/login` LCP at 390px on Slow 4G with a 4x CPU slowdown,
reported against the 2.5 s Core Web Vitals mark and failed past the regression budget declared in that script.
Changes to `docker/nginx.frontend.conf` or `docker/Dockerfile.frontend.prod` therefore trigger Frontend CI, and the
`initial` bundle budget in `frontend/angular.json` is a ratchet set just above the measured size - raise it
deliberately, never to silence a warning.

> **Source of truth split**: For architecture principles, testing requirements, code quality gates, and branching rules — the constitution at [`.specify/memory/constitution.md`](.specify/memory/constitution.md) is authoritative. This file covers **current state only** (what's built, what's running, what's next). When in doubt, constitution wins.

## Project Overview

Finance Sentry is a personal finance aggregation app built as an ASP.NET Core 10 modular monolith + Angular 22 SPA. It integrates with TrueLayer and Monobank for bank data and Binance and Interactive Brokers for investments, with AI-driven portfolio analytics on top.

Sole developer: Denys. Spec-driven development via the **speckit** toolchain (constitution → spec → plan → tasks → implement).

---

## Stack

| Layer | Technology |
|---|---|
| Backend | ASP.NET Core 10 (.NET 10, C# 14), EF Core 10, PostgreSQL 14, hand-rolled CQRS (`FinanceSentry.Core.Cqrs`), Hangfire, Serilog |
| Frontend | Angular 22.2, TypeScript strict, standalone components, NgRx SignalStore (`@ngrx/signals`), lazy-loaded modules |
| UI library | `@lifekit-hq/ui` + `@lifekit-hq/tokens` + `@lifekit-hq/core` + `@lifekit-hq/elements` (`lk-*` PWA prompt elements) — published from [lifekit-common](https://github.com/lifekit-hq/lifekit-common) (GitHub Packages; `NODE_AUTH_TOKEN` needed for installs). Components, `ToastService`, `ErrorMessageService`, `ThemeService`, `AppUpdateService` (PWA) |
| Auth | Stock ASP.NET Core JwtBearer authentication with a default-deny fallback policy (backend; every endpoint declares `[Authorize]`, optionally with a permission policy, or `[AllowAnonymous]`); Owner/Member roles are bundles of `permission` claims in Identity's role-claim table, per-person grants are user claims, read per request + `AuthStore` signal store + functional `authInterceptor` (frontend). Access token lives **in memory only** (store signal); refresh token is an httpOnly/Secure/SameSite=Strict cookie set by the backend. Silent refresh fires on app init. |
| Infra | Docker Compose (single file for full stack) |

---

## How to Run

Everything runs in Docker:

```bash
cd docker
docker compose -f docker-compose.dev.yml up -d --build
```

Startup order enforced by health checks: `postgres → api → frontend`

| Service | URL |
|---|---|
| Frontend (Angular) | http://localhost:4200 |
| Backend API | http://localhost:5001/api/v1 |
| Health check | http://localhost:5001/api/v1/health |
| Swagger | http://localhost:5001/swagger |
| Hangfire dashboard | http://localhost:5001/hangfire |
| PostgreSQL | localhost:5432 (user: finance_user / pw: finance_password / db: finance_sentry) |

For faster frontend iteration, run `ng serve` locally while keeping API + DB in Docker:

```bash
# Terminal 1
cd docker && docker compose -f docker-compose.dev.yml up -d postgres api

# Terminal 2
cd frontend && npm start
```

---

## Mandatory Rules (auto-loaded)

The gates below apply to every change — they are imported into context on every session:

@docs/claude/frontend-rules.md
@docs/claude/backend-rules.md

## Reference Docs (open when relevant)

Not auto-loaded — follow these links when the task touches them:

- [Money semantics](docs/money-semantics.md) — **source of truth for every money calculation** (balance meaning per provider, liability signs, flow windows, snapshot rules); any PR changing money math updates it in the same diff
- [App state & key files](docs/claude/app-state.md) — what's built/running per feature; update the relevant block when a feature lands
- [QA guide](docs/claude/qa.md) — test creds, golden-path scenarios, post-implementation e2e process
- [AI development pipeline](docs/claude/ai-pipeline.md) — Claude/Qwen roles (Qwen path currently disabled)
- [Speckit agent context](docs/claude/speckit-context.md) — machine-appended Active Technologies / Recent Changes (owned by `.specify/scripts/bash/update-agent-context.sh`; never hand-grow this file's sections in AGENTS.md again)
- [Program roadmap & backlog](specs/ROADMAP.md) — destination, radar architecture, unimplemented specs

## QA — Test Account

`test@gmail.com` is no longer a real account; it remains only as a stubbed identity in the route-mocked Playwright specs. Production live checks use the seeded smoke account, whose credentials live only in the CI secrets `E2E_LIVE_EMAIL` / `E2E_LIVE_PASSWORD`. Full scenarios: [QA guide](docs/claude/qa.md).

---

## Naming & Planning Conventions (adopted 2026-08-12)

| Thing | Convention | Example |
|---|---|---|
| Branch | `<type>/<issue#>-<slug>` — type is a conventional-commit type; create via `gh issue develop <n> -b` so the branch links to the issue | `feat/411-canonical-book-figures` |
| Commit / PR title | Conventional commits, scope = module or spec number (release-please parses these). **Squash rule**: a PR's title must carry the *highest-ranked* type among its commits (`feat` > `fix` > `perf`/`refactor` > `docs` > `chore`) — squash makes the title main's only commit message, so a multi-increment branch titled after its last commit misfiles the whole PR in the changelog and can skip the version bump (PR #550: a `feat` branch merged under a `docs` title). Retitle before merging if needed. | `feat(mcp): …`, `fix(040): …` |
| Issue title | Imperative sentence, **no priority prefix** — priority lives in the `P1`/`P2` label and the Project field | `Asset Dossier — per-holding page …` |
| Issue body | Traceability first (destination / source — why this exists), then acceptance criteria (**P1 issues only**; P2/P3 stay one-liners until promoted), then shape in PR count | see #411 |
| Issue metadata | Type (Feature/Bug/Task) + milestone + `P1`/`P2` label + Project "Finance Sentry" Priority/Size fields. Size only on P1 (S=1 PR, M=2–3, L=4+) — never size the fog | — |
| Issue labels | `P1` (firm: sized, acceptance criteria) / `P2` (named fog, unsized); `needs-refinement` = not ready to work; `devclaw-ready` = dispatchable to the autonomous instance; area labels (`frontend`, `backend`, …) | — |
| PR body | What + why, then a **Validation** section stating exactly what was run and its result (the PR template scaffolds this) | see `.github/PULL_REQUEST_TEMPLATE.md` |
| Milestone | `M<n> — <outcome>` — named for the outcome, never a date | `M1 — Ledger earns its keep` |
| Releases | release-please maintains the release PR (version bump for `version.txt` + `frontend/package.json` + API csproj + CHANGELOG); the Weekly Release workflow merges it Mondays 00:07 UTC (`workflow_dispatch` = release now). Both workflows act through the `lifekit-release-bot` GitHub App token (`RELEASE_APP_ID` var + `RELEASE_APP_PRIVATE_KEY` secret) — GITHUB_TOKEN pushes fire no workflows, so with it the release PR never gets its required checks and never merges (#623). The app-token merge is a real push: tag + VPS deploy follow through their normal triggers. Never hand-bump versions or tag ad-hoc. | — |

Backlog planning happens in dedicated sessions (plan-backlog skill); every issue must trace to a destination. Main is protected — all changes land via PR (squash), CI green first, including agent work.

### Gold-standard divergences

Audited against [REPO-STANDARD.md](https://github.com/lifekit-hq/.github/blob/main/REPO-STANDARD.md) (issue #470, 2026-08-29). Where this repo deliberately differs:

- **Root files beyond README/AGENTS**: `CLAUDE.md` (one-line `@AGENTS.md` import), `CHANGELOG.md` + `version.txt` (release-please-owned — `release-type: simple` versions `version.txt`), `devclaw.json` / `global.json` / dotfiles (tool configs). All load-bearing; none are session artifacts.
- **Deploy is continuous, not release-gated**: every merge to main deploys to the VPS (`deploy.yml`). The weekly release cadence governs versioning/CHANGELOG/tags, not shipping — there is no package publishing in this repo, so "publishing hangs off release-created" has nothing to attach to.
- **Pre-commit hook covers the frontend only** (`.husky/pre-commit` via `core.hooksPath`, wired by `frontend`'s `prepare` script): lint-staged only (eslint --fix + prettier on staged files; full lint and format check run in CI). Backend gates (build, tests, `EnforceCodeStyleInBuild`) run in CI only — a `dotnet` build/test cycle is too slow for a commit hook.
- **Coverage floors**: frontend ratchet floors live in `angular.json` (test → `ci` configuration) and gate CI; the backend 80% gate in `backend-ci.yml` is present but commented out (coverage is below the constitution's §II floor — re-enable when it ratchets up).
- **Backend has no format-only lint step**: no `dotnet format --verify-no-changes` in CI or pre-commit; style is enforced at build time via `EnforceCodeStyleInBuild` in `backend/Directory.Build.props` instead.

## Infrastructure catalog (ecosystem rule)

The ecosystem's infrastructure inventory lives in
[`lifekit-dashboard/backend/infra.json`](https://github.com/lifekit-hq/lifekit-dashboard/blob/main/backend/infra.json)
(rendered with live health probes on the dashboard's Infrastructure page).
**Any PR here that adds/removes/moves infrastructure — a container, cron,
workflow, external service, bot, or secret — updates that catalog in the same
change** (companion PR to lifekit-dashboard). `creds` entries name where a
secret lives, never its value.

## Platform first

Use the framework's or platform's standard mechanism before writing a bespoke one, and name the mechanism in the PR. Already in use here, so never reimplement:

- **Backend**: ASP.NET Core Identity (users, roles, claims, lockout); the stock JwtBearer handler and authorization policies; Options binding (`Configure<T>(config.GetSection(...))`); FluentValidation (Auth command validators) and DataAnnotations (BankSync request validators); EF Core migrations; Hangfire jobs; the YARP gateway (`FinanceSentry.Gateway`).
- **Frontend**: Angular router (`provideRouter`); reactive forms; functional HTTP interceptors (`authInterceptor`); signals; `@ngrx/signals` stores.

## Collaboration Style

- Responses must be short and direct. No trailing summaries — Denys can read the diff.
- Lead with the action, skip preamble.
- One fix at a time. Diagnose before pivoting.
- Never change `Host=postgres` to `localhost` to work around Docker issues — fix Docker instead.
- Never modify connection strings or env config as workarounds — fix the root cause.
- Do not create markdown files at the repo root. Only `README.md`, `AGENTS.md` and `CLAUDE.md` belong there. Session artifacts, debug notes, and how-to docs do not get their own files — put relevant content in `README.md` or the appropriate `.specify/` artifact.

## Project layout

```
backend/
  src/
    FinanceSentry.API/               # ASP.NET Core entry point, DI, middleware
    FinanceSentry.Core/              # CQRS interfaces (ICommand, IQuery, etc.), shared domain primitives
    FinanceSentry.Infrastructure/    # Encryption, cross-cutting infra
    FinanceSentry.Mcp/               # MCP server (tools over stdio or HTTP)
    FinanceSentry.Modules.Auth/      # Auth module (JWT, Google OAuth, Identity)
    FinanceSentry.Modules.BankSync/  # Monobank + TrueLayer adapters, accounts, transactions
    FinanceSentry.Modules.CryptoSync/# Binance + Revolut X adapters, crypto holdings
    FinanceSentry.Modules.BrokerageSync/ # IBKR adapter, brokerage holdings
    FinanceSentry.Modules.Wealth/    # Aggregated net-worth queries
    FinanceSentry.Modules.Alerts/    # Alert rules + Hangfire delivery
    FinanceSentry.Modules.Budgets/   # Budget tracking
    FinanceSentry.Modules.Subscriptions/ # Detected recurring subscriptions
    # also: FinanceSentry.Gateway, FinanceSentry.Integration, Modules.{Agent, Analytics, Companion,
    #       Events, Liquidity, Radar, Research, Retention, Risk} — `ls backend/src` is canonical
  tests/
    FinanceSentry.Tests.Unit/        # Pure unit tests (no DB)
    FinanceSentry.Tests.Integration/ # Contract tests via WebApplicationFactory + mocks
                                     # DB-heavy tests tagged [Trait("Category","Integration")]
    FinanceSentry.Mcp.Tests/         # MCP tool contract + schema tests
```

## Test strategy

- **Unit tests** (`FinanceSentry.Tests.Unit`): pure, no DB. Always fast.
- **Contract/integration tests** (`FinanceSentry.Tests.Integration`): use `WebApplicationFactory<Program>`, mock all external I/O (repos via Moq, DB contexts replaced with `UseInMemoryDatabase`). DB-heavy tests are tagged `[Trait("Category","Integration")]`; tests that need a container carry `[DockerRequiredFact]` (`Shared/DockerRequiredFactAttribute.cs`), which skips them at discovery time when no Docker daemon is reachable. A container-backed test takes a fresh database on the run-wide server per image (`Shared/PostgresServer.cs` → `CreateDatabaseAsync`, dropped on dispose) instead of starting its own `PostgreSqlBuilder` container (only a test that needs the server itself to appear mid-test does); classes that run the API's full migrations (which create the cluster-wide `fs_readonly` role) join `PostgresClusterStateCollection` and pass `resetsClusterRoles: true`.
- **MCP tests** (`FinanceSentry.Mcp.Tests`): contract + schema tests for MCP tools; all run in <5 s with no DB.

CI (`backend-ci.yml`) runs the solution **unfiltered** against a `postgres:14-alpine` service
container, so `--filter "Category!=Integration"` is a local convenience, not a gate requirement —
never rely on it to keep a failing test out of CI.

## Key patterns

### HTTP response shapes (contract tests drive these)
Contract tests define `*Shape` records local to the test file and assert on them. If a new field is added to a test's response shape, the corresponding `*Result` record in the Application layer must gain that field too — mismatch causes a subtle null-deserialization failure, not a compile error.

Example of such a mismatch that was fixed: `ConnectBinanceResult` was missing `Message`; the contract test expected `body.Message.Contains("connected")` but got null because JSON deserialised the missing key as null.

### CQRS (MediatR-lite)
- Commands: `ICommand<TResult>` → `ICommandHandler<TCommand, TResult>`
- Queries: `IQuery<TResult>` → `IQueryHandler<TQuery, TResult>`
- All live under `Application/Commands/` or `Application/Queries/` in each module.

### Cross-module migration ordering
Module migrations run module-by-module in a fixed sequence (`backend/src/FinanceSentry.API/Migrations/MigrationExtensions.cs`), not interleaved by timestamp. A migration that reads/writes another module's schema (cross-schema SQL) genuinely depends on that module's schema-creating migration having run first — on a brand-new database this is a real ordering constraint, not just a timestamp coincidence. Grep both modules' migrations for the other's schema name before adding one; if a dependency exists, split the earlier context's `Database.Migrate()` at the dependent migration (`IMigrator.Migrate(targetMigration)`) so the other module's context can run in between, and skip that partial step once the target is already applied — `Migrate(target)` is an exact target, not an upper bound, so on an already-migrated database it would roll back (and drop data from) every later migration on each boot. See the Research/Risk split there and `FreshDatabaseMigrationTests` (`backend/tests/FinanceSentry.Tests.Integration/Migrations/`) for the pattern. A migration that fails against a *reachable* database now aborts startup (`StartupMigrationException`, uncaught, non-zero exit) instead of leaving the API serving on a half-migrated schema (#661/#664/#667) — the log line and exception name the `DbContext` and the specific migration that failed. If the database connection is lost after earlier modules have already migrated, that half-migrated startup halts the same way. A database that is not reachable at all before anything has migrated is *not* fatal: `MigrateContext` probes connectivity first and skips (logs and continues) so the existing `/api/v1/health/ready` "database" check can report that path — this is also what lets `WebApplicationFactory` fixtures that point a context at a deliberately unreachable connection string (e.g. `ObservabilityApiFactory`) boot without a real Postgres. A reachable server whose database does not exist yet is not skipped — `Migrate()` creates it. That skip is recorded in `StartupMigrationStatus` and surfaced by the `migrations` readiness check (`StartupMigrationsHealthCheck`, same `/api/v1/health/ready` report): once the database is back and the schema is behind it stays Unhealthy and lists the pending migrations per module (Healthy if nothing turned out to be pending), so "serving a schema this process never migrated" is named at the readiness endpoint instead of only as unrelated 500s — the fix is a restart, the check never migrates on its own (`docs/OPERATIONS_RUNBOOK.md` §7). Operator side (what `deploy.sh` reports and how to recover): `docs/OPERATIONS_RUNBOOK.md` §8.

### Authentication
The API host uses the stock JwtBearer handler (`FinanceSentry.API/Authentication/ApiAuthenticationExtensions.cs`): it reads the access-token cookie (`__Host-fs_access_token` in production, `fs_access_token` elsewhere - `AuthCookies`; the Authorization header also works), accepts `aud=app` tokens, and builds the principal from the local account the `sub` names on every request, so a token whose account is missing or locked out gets 401 and role or permission changes apply on the next request. The fallback policy denies everyone: every endpoint declares `[Authorize]` (optionally with an `AuthPolicies` permission policy) or `[AllowAnonymous]`, and `ApiAuthenticationPipelineTests` pins both the anonymous list and that coverage. Authorization is two roles (Owner, Member) whose `permission` claims live in Identity's role-claim table (`Permissions.ByRole`, converged at startup by `RoleSeeder`), plus per-person grants in the user-claim table; `RoleFeatureMatrixTests` pins the role x feature matrix. In tests, inject the JWT via `client.DefaultRequestHeaders.Add("Cookie", $"fs_access_token={jwt}")` and seed its user first (`TestUsers.EnsureExists(services, userId)` - a Member unless a role is passed).

### Encryption
`ICredentialEncryptionService` is injected in handlers that store API keys. The test harness wires settings:
```
Encryption:CurrentKeyVersion = "1"
Encryption:Keys:1 = "<base64-key>"
Deduplication:MasterKeyBase64 = "<base64-key>"
```

## Gotchas

- `dotnet build --no-restore` fails on a fresh clone — always restore first.
- In-memory DB per test class: each `WebApplicationFactory` subclass uses a unique GUID database name to avoid cross-test state bleed.
- `MockBehavior.Loose` is used in factory mocks; setup only what the specific test path needs.
- `[Trait("Category","Integration")]` is the convention for skipping DB-live tests — don't change it.
- A bare `.dockerignore` pattern (`obj`, `node_modules`, `dist`) matches only at the context root in this BuildKit version, so every nested build output is listed as `**/<name>` (`**/bin`, `**/obj`, `**/node_modules`, `**/dist`, `**/.angular`, `**/coverage`, ...). A dirty build context is not only a local worktree problem: Frontend CI builds `docker/Dockerfile.frontend.prod` for the LCP gate in the same job as `npm ci`, the build and the test runs. A missing pattern shows up as a stray `obj/` copied over a freshly-restored one (a confusing NETSDK1064 "package not found" error unrelated to the restore) or as the runner's `frontend/node_modules` entering the image, where `npm install` then reconciles a foreign-platform tree instead of installing fresh.
- `docker/Dockerfile`, `Dockerfile.mcp` and `Dockerfile.gateway` set `ENV NUGET_PACKAGES=/src/.nuget/packages` and restore straight into the build layer (csproj-first, then `build --no-restore` + `publish --no-build`) instead of a `--mount=type=cache` — a cache mount's contents aren't guaranteed to persist across the image's later `RUN` steps under BuildKit GC/concurrent-build pressure, which was silently dropping packages between restore and publish.
- `dotnet tool restore` (used for `reportgenerator` in Backend CI) needs its manifest at `.config/dotnet-tools.json` at the **repo root**, not under `backend/` — CI steps run from repo root and `dotnet tool restore` only walks up from CWD.

## MCP Verification

Verified 2026-09-03 via `dotnet test FinanceSentry.sln --no-build -c Release` (no filter; the
plain command needs no flags — `backend/Directory.Build.rsp` pins one build node, because the default
parallel run needs more than 4 GB of RAM).

| Project | Passed | Skipped | Failed |
|---|---|---|---|
| FinanceSentry.Tests.Unit | 541 | 0 | 0 |
| FinanceSentry.Tests.Integration | 120 | 6 | 0 |
| FinanceSentry.Mcp.Tests | 103 | 0 | 0 |
| FinanceSentry.Modules.Research.Tests | 204 | 2 | 0 |
| FinanceSentry.Modules.Radar.Tests | 80 | 0 | 0 |
| FinanceSentry.Modules.Risk.Tests | 37 | 0 | 0 |
| FinanceSentry.Modules.Retention.Tests | 37 | 0 | 0 |
| FinanceSentry.Modules.Agent.Tests | 35 | 0 | 0 |
| FinanceSentry.Modules.Analytics.Tests | 35 | 0 | 0 |
| FinanceSentry.Modules.Companion.Tests | 24 | 0 | 0 |
| FinanceSentry.Gateway.Tests | 6 | 0 | 0 |

Skips are dependency-gated, not disabled tests: 2 Docker-gated (`[DockerRequiredFact]`) and 6
needing a live Postgres or a live external page.

### Registered MCP tools (58 total)

Full tool catalogue (input parameters, return schemas, real/stub): [`docs/mcp.md`](docs/mcp.md).
Canonical list: `backend/tests/FinanceSentry.Mcp.Tests/ContractTests/ToolNameContractTests.cs`.

## Frontend attribute ordering

Angular ESLint enforces `@angular-eslint/template/attributes-order`. The expected order is: bound properties `[prop]` first, then plain attribute strings (`icon`, `variant`), then event bindings `(event)`. Structural slot markers (like `cta`, `leading`, `trailing` on projected children) come after event bindings. Run `ng lint` or let lint-staged auto-fix before committing.

## Memory (vault)

Durable knowledge about this project lives in the vault, not in provider memory: `~/memory/projects/finance-sentry/` - `plan.md` (facts), `STATUS.md` (in-flight, only while parked), `log.md` (dated events). Read `plan.md` + `STATUS.md` when starting work here; verify live state live (`gh`, `docker ps`). Write durable decisions and gotchas back to those pages in the same session (contract: `~/memory/README.md`). Claude Code auto-memory is disabled by policy (`CLAUDE_CODE_DISABLE_AUTO_MEMORY=1`); Codex/other agents follow the same pointer via this file.
