# Finance Sentry — Agent Harness

## Quick-start

```bash
# Restore + build (required on a fresh clone or after switching branches)
cd backend && dotnet restore FinanceSentry.sln
dotnet build FinanceSentry.sln --no-restore -c Release

# Run tests — no filter. CI runs the full solution too (509); container-backed tests
# report themselves as Skipped where no Docker daemon is reachable.
dotnet test FinanceSentry.sln --no-build -c Release
```

### Frontend Playwright e2e (required when touching app-surface UI)

```bash
# Requires NODE_AUTH_TOKEN (read:packages) for @lifekit-hq/* install and Angular build.
# Also requires libXfixes.so.3 — see the "Frontend test environment gotcha" section below.
cd frontend
npm ci
npm run build
LD_LIBRARY_PATH=/tmp:$LD_LIBRARY_PATH npx playwright test --reporter=json
```

Sandbox shortcut (no NODE_AUTH_TOKEN): build @lifekit-hq/* from lifekit-common source, install
from tarballs, build the Angular app, then run Playwright. `libXfixes.so.3` is on the system
at `/usr/lib/aarch64-linux-gnu/libXfixes.so.3` — copy it to `/tmp/` once per session:

```bash
# One-time per session
cp /usr/lib/aarch64-linux-gnu/libXfixes.so.3 /tmp/

# Build @lifekit-hq/* from source and install as tarballs (no GitHub Packages auth needed)
cd /tmp && git clone --depth 1 https://github.com/lifekit-hq/lifekit-common.git
cd /tmp/lifekit-common && NODE_OPTIONS="--max-old-space-size=2048" npm install --no-fund --no-audit
npx ng build @lifekit-hq/charts-core @lifekit-hq/core @lifekit-hq/ui
# Pack each dist and the source-only packages
cd dist/lifekit-hq/charts-core && npm pack --pack-destination /tmp/
cd /tmp/lifekit-common/dist/lifekit-hq/core && npm pack --pack-destination /tmp/
cd /tmp/lifekit-common/dist/lifekit-hq/ui && npm pack --pack-destination /tmp/
cd /tmp/lifekit-common/projects/tokens && npm pack --pack-destination /tmp/
cd /tmp/lifekit-common/projects/config && npm pack --pack-destination /tmp/

# Strip @lifekit-hq/charts-core dep from UI package.json (it's inlined in the bundle)
# then install all tarballs in finance-sentry frontend
cd /workspace/frontend
NODE_OPTIONS="--max-old-space-size=2048" npm install \
  /tmp/lifekit-hq-tokens-*.tgz /tmp/lifekit-hq-core-*.tgz \
  /tmp/lifekit-hq-charts-core-*.tgz /tmp/lifekit-hq-ui-*.tgz \
  /tmp/lifekit-hq-config-*.tgz --legacy-peer-deps --prefer-offline
node scripts/patch-lifekit-ui.js   # npm install does not reliably fire the postinstall

# Build the Angular app, then run Playwright
NODE_OPTIONS="--max-old-space-size=2048" npx ng build --configuration=production
mkdir -p playwright-report
PLAYWRIGHT_JSON_OUTPUT_NAME=/workspace/frontend/playwright-report/results.json \
PLAYWRIGHT_BROWSERS_PATH=/home/agent/.cache/ms-playwright \
LD_LIBRARY_PATH=/tmp:$LD_LIBRARY_PATH \
/workspace/frontend/node_modules/.bin/playwright test --reporter=json
```

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
Module migrations run module-by-module in a fixed sequence (`backend/src/FinanceSentry.API/Migrations/MigrationExtensions.cs`), not interleaved by timestamp. A migration that reads/writes another module's schema (cross-schema SQL) genuinely depends on that module's schema-creating migration having run first — on a brand-new database this is a real ordering constraint, not just a timestamp coincidence. Grep both modules' migrations for the other's schema name before adding one; if a dependency exists, split the earlier context's `Database.Migrate()` at the dependent migration (`IMigrator.Migrate(targetMigration)`) so the other module's context can run in between, and skip that partial step once the target is already applied — `Migrate(target)` is an exact target, not an upper bound, so on an already-migrated database it would roll back (and drop data from) every later migration on each boot. See the Research/Risk split there and `FreshDatabaseMigrationTests` (`backend/tests/FinanceSentry.Tests.Integration/Migrations/`) for the pattern. A migration that fails against a *reachable* database now aborts startup (`StartupMigrationException`, uncaught, non-zero exit) instead of leaving the API serving on a half-migrated schema (#661/#664/#667) — the log line and exception name the `DbContext` and the specific migration that failed. If the database connection is lost after earlier modules have already migrated, that half-migrated startup halts the same way. A database that is not reachable at all before anything has migrated is *not* fatal: `MigrateContext` probes connectivity first and skips (logs and continues) so the existing `/api/v1/health/ready` "database" check can report that path — this is also what lets `WebApplicationFactory` fixtures that point a context at a deliberately unreachable connection string (e.g. `ObservabilityApiFactory`) boot without a real Postgres. A reachable server whose database does not exist yet is not skipped — `Migrate()` creates it. Operator side (what `deploy.sh` reports and how to recover): `docs/OPERATIONS_RUNBOOK.md` §8.

### Auth middleware
`JwtAuthenticationMiddleware` reads `fs_access_token` cookie. In tests, inject the JWT via `client.DefaultRequestHeaders.Add("Cookie", $"fs_access_token={jwt}")`.

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

## MCP Verification

Verified 2026-09-03 via `dotnet test FinanceSentry.sln --no-build -c Release -m:1` (no filter;
`-m:1` keeps the run inside a 2-CPU / 4 GB sandbox — the default parallel run gets OOM-killed).

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

## Frontend UI primitives — ng-zorro-antd

`ng-zorro-antd` **21.2.2** is a real, installed runtime dependency (`frontend/package.json` line 52; resolved entry in `frontend/package-lock.json`). It serves as the low-level widget primitive layer for `@dsdevq-common/ui` — library components wrap `nz-*` elements rather than building raw HTML widgets from scratch.

**Reference usage:** `SelectComponent` (`frontend/projects/dsdevq-common/ui/src/lib/components/select/select.component.ts`) imports `NzSelectModule` from `ng-zorro-antd/select` and renders `<nz-select>` / `<nz-option>` in its template. This component has a passing Vitest spec that proves the dependency resolves and renders end-to-end in the test environment.

Architecture direction: new `cmn-*` library components that need a complex interactive primitive (date-picker, tree-select, cascader, etc.) should prefer an `nz-*` base over hand-rolling the behaviour. Design token coexistence (`@dsdevq-common/config` Tailwind tokens vs `ng-zorro-antd` CSS vars) is a separate, deferred slice — do not resolve it implicitly when adding new components.

## Frontend test environment gotcha

`libXfixes.so.3` is installed at `/usr/lib/aarch64-linux-gnu/libXfixes.so.3` in the sandbox. Copy
it to `/tmp/` so Chromium can find it:

```bash
cp /usr/lib/aarch64-linux-gnu/libXfixes.so.3 /tmp/
# Then run Playwright with LD_LIBRARY_PATH=/tmp:$LD_LIBRARY_PATH
```

The husky pre-commit hook runs lint-staged + `npm run lint` + `npm run format:check` — it does
**not** run `npm ci`, so it passes without `--no-verify` once `frontend/node_modules` exists. It
fails with `eslint … ENOENT` (not a lint error) when frontend files are staged and
`frontend/node_modules` is absent.

## Frontend attribute ordering

Angular ESLint enforces `@angular-eslint/template/attributes-order`. The expected order is: bound properties `[prop]` first, then plain attribute strings (`icon`, `variant`), then event bindings `(event)`. Structural slot markers (like `cta`, `leading`, `trailing` on projected children) come after event bindings. Run `ng lint` or let lint-staged auto-fix before committing.

## Memory (vault)

Durable project knowledge lives in `~/memory/projects/finance-sentry/` (`plan.md` facts, `STATUS.md` in-flight, `log.md` events) - read `plan.md` + `STATUS.md` when starting work here and write durable decisions back there; see `~/memory/README.md` for the contract. Provider-local memories are disabled by policy.
