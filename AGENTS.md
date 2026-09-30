# Finance Sentry — Agent Harness

## Quick-start

```bash
# Restore + build (required on a fresh clone or after switching branches)
cd backend && dotnet restore FinanceSentry.sln
dotnet build FinanceSentry.sln --no-restore -c Release

# Run tests — no filter. CI runs the full solution too; container-backed tests
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

Commit hook rule: `.husky/pre-commit` (wired by `frontend`'s `prepare` script) runs lint-staged,
the full frontend lint and the Prettier check only when `frontend/src/**` is staged (about 50 s),
and never runs `npm ci` — it assumes `frontend/node_modules` exists and is the repo's commit gate,
not something to bypass.

## Project layout

```
backend/
  src/
    FinanceSentry.API/               # ASP.NET Core entry point, DI, middleware
    FinanceSentry.Core/              # CQRS interfaces (ICommand, IQuery, etc.), shared domain primitives
    FinanceSentry.Infrastructure/    # Encryption, cross-cutting infra
    FinanceSentry.Mcp/               # MCP server (7 tools over stdio or HTTP)
    FinanceSentry.Modules.Auth/      # Auth module (JWT, Google OAuth, Identity)
    FinanceSentry.Modules.BankSync/  # Monobank + TrueLayer adapters, accounts, transactions
    FinanceSentry.Modules.CryptoSync/# Binance + Revolut X adapters, crypto holdings
    FinanceSentry.Modules.BrokerageSync/ # IBKR adapter, brokerage holdings
    FinanceSentry.Modules.Wealth/    # Aggregated net-worth queries
    FinanceSentry.Modules.Alerts/    # Alert rules + Hangfire delivery
    FinanceSentry.Modules.Budgets/   # Budget tracking
    FinanceSentry.Modules.Subscriptions/ # Detected recurring subscriptions
  tests/
    FinanceSentry.Tests.Unit/        # Pure unit tests (223 tests, no DB)
    FinanceSentry.Tests.Integration/ # Contract tests via WebApplicationFactory + mocks
                                     # DB-heavy tests tagged [Trait("Category","Integration")]
    FinanceSentry.Mcp.Tests/         # MCP tool contract + schema tests (43 tests)
```

## Test strategy

- **Unit tests** (`FinanceSentry.Tests.Unit`): pure, no DB. Always fast.
- **Contract/integration tests** (`FinanceSentry.Tests.Integration`): use `WebApplicationFactory<Program>`, mock all external I/O (repos via Moq, DB contexts replaced with `UseInMemoryDatabase`). DB-heavy tests are tagged `[Trait("Category","Integration")]`; tests that need a container carry `[DockerRequiredFact]` (`Shared/DockerRequiredFactAttribute.cs`), which skips them at discovery time when no Docker daemon is reachable.
- **MCP tests** (`FinanceSentry.Mcp.Tests`): contract + schema tests for MCP tools; all 43 run in <5 s with no DB.

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
The API host uses the stock JwtBearer handler (`FinanceSentry.API/Authentication/ApiAuthenticationExtensions.cs`): it reads the `fs_access_token` cookie (the Authorization header also works), accepts `aud=app` tokens, and builds the principal from the local account the `sub` names, so a token whose account is missing or locked out gets 401. A fallback policy requires an authenticated user everywhere; public endpoints carry `[AllowAnonymous]`, and `ApiAuthenticationPipelineTests` pins that list. In tests, inject the JWT via `client.DefaultRequestHeaders.Add("Cookie", $"fs_access_token={jwt}")` and seed its user first (`TestUsers.EnsureExists(services, userId)`).

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
- `.dockerignore`'s bare `bin`/`obj` patterns do NOT exclude nested `backend/**/bin`/`backend/**/obj` in this BuildKit version (needs `**/bin`/`**/obj`). Only bites local `docker build` from a dirty worktree — CI always builds from a fresh checkout — but a stray local `obj/` can get copied over a freshly-restored one and produce a confusing NETSDK1064 "package not found" error that has nothing to do with the actual restore.
- `docker/Dockerfile`, `Dockerfile.mcp` and `Dockerfile.gateway` set `ENV NUGET_PACKAGES=/src/.nuget/packages` and restore straight into the build layer (csproj-first, then `build --no-restore` + `publish --no-build`) instead of a `--mount=type=cache` — a cache mount's contents aren't guaranteed to persist across the image's later `RUN` steps under BuildKit GC/concurrent-build pressure, which was silently dropping packages between restore and publish.
- `dotnet tool restore` (used for `reportgenerator` in Backend CI) needs its manifest at `.config/dotnet-tools.json` at the **repo root**, not under `backend/` — CI steps run from repo root and `dotnet tool restore` only walks up from CWD.

## MCP Verification

Verified 2026-09-03 via `dotnet test FinanceSentry.sln --no-build -c Release -m:1` (no filter;
`-m:1` serialises the test projects — the default parallel run needs more than 4 GB of RAM).

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

Durable project knowledge lives in `~/memory/projects/finance-sentry/` (`plan.md` facts, `STATUS.md` in-flight, `log.md` events) - read `plan.md` + `STATUS.md` when starting work here and write durable decisions back there; see `~/memory/README.md` for the contract. Provider-local memories are disabled by policy.
