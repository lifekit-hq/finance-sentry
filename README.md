# Finance Sentry

[![Release](https://img.shields.io/github/v/release/lifekit-hq/finance-sentry?sort=semver)](https://github.com/lifekit-hq/finance-sentry/releases)
[![Backend CI](https://github.com/lifekit-hq/finance-sentry/actions/workflows/backend-ci.yml/badge.svg)](https://github.com/lifekit-hq/finance-sentry/actions/workflows/backend-ci.yml)
[![Frontend CI](https://github.com/lifekit-hq/finance-sentry/actions/workflows/frontend-ci.yml/badge.svg)](https://github.com/lifekit-hq/finance-sentry/actions/workflows/frontend-ci.yml)
[![Deploy](https://github.com/lifekit-hq/finance-sentry/actions/workflows/deploy.yml/badge.svg)](https://github.com/lifekit-hq/finance-sentry/actions/workflows/deploy.yml)

Personal finance aggregation platform — bank accounts, crypto, brokerage, budgets, subscriptions, and alerts in one place.

## Features

- **Multi-provider sync** — TrueLayer (EU/UK open banking), Monobank, Binance, Revolut X, Interactive Brokers
- **Automatic transaction sync** with cursor-based incremental updates and webhook support
- **Subscription detection** via a merchant/cadence heuristic over synced transactions (installment-aware for Monobank)
- **Budget tracking** with spending analysis per category
- **Alerts** — unusual spend detection and configurable thresholds
- **Multi-currency dashboard** with aggregated net worth, money flow, and category breakdown
- **AES-256-GCM encryption** for all stored credentials
- **Full audit logging** of all data access events

## Prerequisites

- Docker & Docker Compose

## Local Development

All `docker compose` commands assume `cd docker` first.

### Service map

| Service | Container | Port |
|---|---|---|
| Frontend (Angular dev server) | `finance-sentry-frontend` | 4200 |
| Backend API (.NET 9) | `finance-sentry-api` | 5001 (host) → 5000 (container) |
| PostgreSQL 14 | `finance-sentry-postgres` | 5432 |

Startup order enforced by health checks: `postgres → api → frontend`.

| URL | What |
|---|---|
| http://localhost:4200 | Angular SPA |
| http://localhost:5001/api/v1 | REST API |
| http://localhost:5001/api/v1/health | Liveness probe |
| http://localhost:5001/api/v1/health/ready | Readiness probe (per-dependency: `database`, `hangfire`) |
| http://localhost:5001/metrics | Prometheus exposition (scrape-only) |
| http://localhost:5001/swagger | Swagger UI |
| http://localhost:5001/hangfire | Hangfire dashboard |
| http://localhost:3000 | Grafana (dashboards) — **dev compose only**; on the VPS it lives in lifekit-stack |
| http://localhost:9090 | Prometheus — dev compose only (same) |
| http://localhost:3100 | Loki (log store) — dev compose only (same) |

### Run everything

```bash
docker compose -f docker-compose.dev.yml up -d --build       # first time / after backend changes
docker compose -f docker-compose.dev.yml up -d               # subsequent runs
```

### Run services separately

```bash
docker compose -f docker-compose.dev.yml up -d postgres api  # db + api only
```

For native frontend with hot reload:

```bash
docker compose -f docker-compose.dev.yml up -d postgres api
cd ../frontend && npm start
```

### Rebuild

```bash
docker compose -f docker-compose.dev.yml up -d --build api         # rebuild and restart api
docker compose -f docker-compose.dev.yml build --no-cache frontend  # clean frontend rebuild
```

Backend (`.cs`) edits require an api rebuild. Frontend (`.ts`/`.html`) edits hot-reload via the bind mount.

### Logs / shell

```bash
docker compose -f docker-compose.dev.yml logs -f api
docker compose -f docker-compose.dev.yml ps

docker exec -it finance-sentry-api sh
docker exec -it finance-sentry-postgres psql -U finance_user -d finance_sentry
```

### Stop / clean

```bash
docker compose -f docker-compose.dev.yml down                 # stop + remove containers
docker compose -f docker-compose.dev.yml down -v              # also drop postgres volume (wipes DB)
```

### Environment variables

| Variable | Description |
|---|---|
| `ConnectionStrings__Default` | PostgreSQL DSN |
| `Deduplication__MasterKeyBase64` | AES-256 master key (base64, 32 bytes) |
| `Jwt__Secret` | JWT signing secret (≥32 chars) |

## Observability (feature 023)

The **dev** compose stack runs **Prometheus + Grafana + Loki** alongside the app so failures announce
themselves instead of being found days later via `ssh`+`grep`.

On the VPS that trio moved out of this repo on 2026-09-06
([lifekit-stack#133](https://github.com/lifekit-hq/lifekit-stack/pull/133)): it was box infrastructure
living in one product's compose file, on this product's private network, which is why Prometheus could
only ever see one target and nothing else on the box had alerting at all. It now scrapes devclaw too and
carries the box's alert rules. What stays this repo's job is unchanged — the `/metrics` endpoint, the
Loki sink, and the dashboards below.

- **Metrics** — the API is instrumented with OpenTelemetry and exposes Prometheus exposition at `/metrics`
  (ASP.NET Core request rate/latency/errors, .NET runtime, and custom `finance_jobs_*` per-job counters).
  Prometheus scrapes it every 15s; retention ~30d, hard-capped at 5GB.
- **Logs** — Serilog ships structured logs to Loki (fire-and-forget; a shipping outage never affects
  requests). EF Core SQL is suppressed to `Warning` by default (raise via
  `Serilog:MinimumLevel:Override` in config). Retention ~14d, size-capped.
- **Dashboards** — provisioned as code under `docker/observability/grafana/provisioning/`, one home for
  both Grafanas: the dev compose mounts the directory, and `deploy.sh` publishes the same JSON to the
  host directory lifekit-stack's Grafana provisions from. The main dashboard ("Health at a glance")
  answers *is it healthy now, did last night's jobs run?* at a glance; the availability panel turns red
  within ~60s of an API outage.
- **Jobs** — Hangfire storage moved to PostgreSQL (`hangfire` schema) so job history/schedule survive
  restarts. The Hangfire dashboard at `/hangfire` is loopback/Tailscale-only outside Development.

```bash
# bring the stack up (adds loki, prometheus, grafana)
cd docker && docker compose -f docker-compose.dev.yml up -d --build

curl -s http://localhost:5001/metrics | grep finance_jobs_        # custom job metrics present
curl -s http://localhost:5001/api/v1/health/ready                 # {"status":"Healthy","checks":[...]}
# open http://localhost:3000 (admin/admin by default) → Finance Sentry → Health at a glance
```

**Production notes** — Grafana + Hangfire dashboards are reachable only over Tailscale serve (not the
public funnel); `/metrics` is scrape-only. `GRAFANA_ADMIN_USER` / `GRAFANA_ADMIN_PASSWORD` /
`GRAFANA_ROOT_URL` now belong to **lifekit-stack's** env file, not this repo's `.env.sops`;
`Observability__Loki__Url` stays here (it defaults to `http://loki:3100`, which still resolves — the api
is on the shared `openclaw` bridge where lifekit-stack's Loki lives). Alert rules are no longer deferred
and no longer this repo's: they are provisioned files in lifekit-stack. Job silent-failure alerting (US4,
N consecutive failures → Telegram) remains the app-side slice.

## Running Tests

```bash
# Backend unit + integration tests
cd backend && dotnet test

# Frontend unit tests (Vitest)
cd frontend && npm test
```

## Architecture

```
frontend/                             Angular 21 SPA — strict TypeScript, standalone components
                                      NgRx SignalStore, lazy-loaded feature modules
backend/
  src/
    FinanceSentry.API/                ASP.NET Core 9 host — middleware, DI, migration runner
    FinanceSentry.Core/               Shared interfaces and domain primitives
    FinanceSentry.Infrastructure/     Cross-cutting: encryption, logging
    FinanceSentry.Modules.Auth/       Registration, login, Google OAuth, JWT + refresh tokens
    FinanceSentry.Modules.BankSync/   Monobank + TrueLayer sync, transactions, dashboard
    FinanceSentry.Modules.CryptoSync/ Binance + Revolut X integrations, crypto holdings
    FinanceSentry.Modules.BrokerageSync/ IBKR Client Portal, brokerage holdings
    FinanceSentry.Modules.Budgets/    Budget definitions, spend tracking per category
    FinanceSentry.Modules.Alerts/     Alert rules, unusual spend detection, nightly job
    FinanceSentry.Modules.Subscriptions/ Recurring charge detection (heuristic, installment-aware)
docker/
  docker-compose.dev.yml             Full stack (postgres + api + frontend)
  Dockerfile                         Multi-stage backend build with BuildKit cache mounts
  Dockerfile.frontend                Node 22 Alpine, ng serve
```

Each module follows the same internal structure: `Domain/` → `Application/` (CQRS via MediatR) → `Infrastructure/` (EF Core, external clients, Hangfire jobs) → `API/` (controllers). Modules register themselves via `IModuleRegistrar` (every host: API and MCP), `IWorkerRegistrar` (API host only: hosted services, key rotation, the credential key) and `IJobRegistrar` (API host only: Hangfire schedules) — no manual wiring in `Program.cs`.

## Versioning & Releases

Finance Sentry ships as a single product with one [SemVer](https://semver.org/) version (`vX.Y.Z`), managed by [release-please](https://github.com/googleapis/release-please):

- Every change lands on `main` as a small, self-contained [Conventional Commit](https://www.conventionalcommits.org/) (`feat:` → minor bump, `fix:` → patch, `feat!:` → major).
- Each push to `main` is deployed to production automatically (`deploy.yml`).
- release-please maintains an open **release PR** that accumulates commits into a draft [CHANGELOG](CHANGELOG.md) entry. Merging it cuts the release: tag `vX.Y.Z`, GitHub Release with notes, and version bumps in `version.txt`, `frontend/package.json`, and `FinanceSentry.API.csproj` — all in one automated commit.
- Deploys are continuous; releases are milestones. Cut one whenever a meaningful increment is complete (typically after each feature spec lands).

The current version lives in [`version.txt`](version.txt). Release history: [GitHub Releases](https://github.com/lifekit-hq/finance-sentry/releases) · [CHANGELOG.md](CHANGELOG.md).

## Development Workflow

This project uses **speckit** — a spec-driven development toolchain built on top of Claude Code.

```
constitution → spec → plan → tasks → implement
```

| Command | Purpose |
|---|---|
| `/speckit.specify` | Create or update a feature spec |
| `/speckit.plan` | Generate implementation design from the spec |
| `/speckit.tasks` | Generate ordered task list from the plan |
| `/speckit.implement` | Execute tasks from `tasks.md` |
| `/speckit.analyze` | Cross-artifact consistency check |

Specs live in `.specify/specs/<feature>/`. Architecture principles and quality gates are in [`.specify/memory/constitution.md`](.specify/memory/constitution.md).

## MCP Server

Finance Sentry ships a read-only **Model Context Protocol (MCP) server** (`FinanceSentry.Mcp`) that lets MCP-capable clients — Claude Desktop, Claude Code, or any MCP host — query live financial data without going through the REST API. It exposes 11 tools covering account balances, transactions, budgets, alerts, portfolio positions, detected subscriptions, and sync health. Four additional tools are stubs that return `{ status: "not_yet_available" }` while their backing modules are still under development.

### Connect via Claude Desktop (stdio transport)

Add the following block to your `claude_desktop_config.json` (usually `~/Library/Application Support/Claude/claude_desktop_config.json` on macOS):

```json
{
  "mcpServers": {
    "finance-sentry": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "/absolute/path/to/finance-sentry/backend/src/FinanceSentry.Mcp"
      ],
      "env": {
        "MCP_TRANSPORT": "stdio",
        "MCP_CONNECTION_STRING": "Host=localhost;Port=5432;Database=finance_sentry;Username=finance_user;Password=finance_password"
      }
    }
  }
}
```

Replace `/absolute/path/to/finance-sentry` with the real path on your machine. Make sure the local Docker stack is running (`docker compose -f docker/docker-compose.dev.yml up -d postgres`) so the MCP server can reach PostgreSQL.

For HTTP/SSE transport (networked or multi-client deployments), set `MCP_TRANSPORT=http` and optionally `MCP_HTTP_PORT=5100`. Copy `.env.example` to `.env` and adjust the values before starting the server.

See [docs/mcp.md](docs/mcp.md) for the full tool catalogue — input parameters, return schemas, and real-vs-stub status for each tool.
