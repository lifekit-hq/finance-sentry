# Tasks — Spec 048: Platform Contract Conformance

## [US5] JSON stdout with trace id (shared plumbing first — US2/US3 build on it)

- [x] T001 Move `TraceEnricher` to `FinanceSentry.Core.Observability`; add `Serilog` to Core; fix
      the Infrastructure and unit-test usings
- [x] T002 `Serilog.Formatting.Compact` 2.0.0 into `Directory.Packages.props` + Infrastructure;
      `SerilogConfiguration` writes `RenderedCompactJsonFormatter` to the console and gains
      `For(appName)`
- [x] T003 `JsonConsoleLogLineTests` — a line written through the compact formatter with the
      enricher inside an `Activity` parses as JSON with `@t`, `@m`, `TraceId`, `SpanId`; outside an
      `Activity` it has no `TraceId`

## [US2] Gateway readiness

- [x] T004 `GatewayReadiness.Evaluate(IEnumerable<ClusterState>)` → `GatewayReadinessReport`
- [x] T005 `Program.cs` — Serilog JSON console (`Serilog.AspNetCore`, Core reference), map
      `GET /gateway/ready` (200/503 JSON)
- [x] T006 `GatewayReadinessTests` — all clusters available → ready; one empty cluster → not
      ready, that cluster reads `available: 0`; no clusters → not ready
- [x] T007 `GatewayEndpointContractTests` (`WebApplicationFactory<Program>`, active health checks
      off, OTLP off) — `/gateway/health` 200 JSON, `/gateway/ready` 200 JSON with every configured
      cluster, `/metrics` 200 Prometheus text

## [US3] MCP anonymous platform endpoints

- [x] T008 `McpJwtAuthenticationMiddleware` — exact-match anonymous set `/health`, `/ready`,
      `/metrics`
- [x] T009 `McpPlatformEndpoints` — `AddMcpPlatformEndpoints` (Npgsql `database` check tagged
      `ready`; OTel metrics + Prometheus exporter, service `finance-sentry-mcp`) and
      `MapMcpPlatformEndpoints` (`/health`, `/ready` via `ReadinessResponseWriter`, `/metrics`)
- [x] T010 `Program.cs` http branch — `UseSerilog(SerilogConfiguration.For("finance-sentry-mcp"))`,
      request logging, platform endpoints
- [x] T011 `Dockerfile.mcp` — `apk add --no-cache krb5-libs`
- [x] T012 `McpJwtAuthenticationMiddlewareTests` — anonymous paths pass through without a token;
      `/health/x` and `/HEALTH` still 401
- [x] T013 `McpPlatformEndpointsTests` (`TestServer`) — `/health` 200 JSON; `/ready` with an
      unreachable connection string 503 JSON naming `database: Unhealthy`; `/metrics` 200
      Prometheus content type

## [US4] Frontend nginx

- [x] T014 `nginx.frontend.conf` — JSON `log_format` with `traceparent`, `access_log /dev/stdout`,
      `/healthz`, `/readyz`, `/metrics` (njs), internal `/stub_status`
- [x] T015 `nginx.metrics.js` — `stub_status` → Prometheus exposition
- [x] T016 `Dockerfile.frontend.prod` — `load_module` + `error_log warn` in `nginx.conf`, copy the
      njs file, `NGINX_ENTRYPOINT_QUIET_LOGS=1`

## [US1] + [US6] Compose declaration and deploy gate

- [x] T017 `docker-compose.prod.yml` — `lifekit.contract.*` labels on all five services; frontend
      joins `openclaw` (`compose_default`)
- [x] T018 `deploy.sh` — static gate before `up`; runtime gate after the health wait with a bounded
      retry
- [x] T019 `docs/claude/app-state.md` — platform-contract block; spec status

## Verification

- [x] `dotnet build FinanceSentry.sln -m:1` (in `mcr.microsoft.com/dotnet/sdk:10.0`) — 0 warnings, 0 errors
- [x] `dotnet test FinanceSentry.sln --no-build -m:1` — **1995 passed, 0 failed, 12 skipped**
      (Unit 1092 · Integration 157 · Mcp 134 · Gateway 12 · Research 310 · Radar 114 · others)
- [x] Static gate on the prod compose (`config --format json | platform-contract.py --static -`):
      `api ok · frontend ok · gateway ok · mcp ok · postgres none — every service is declared`
- [x] Scratch `nginx:alpine` container (`--network none`, same `sed`s as the Dockerfile, conf + njs
      bind-mounted): `/healthz` 200 `application/json`, `/readyz` 200 `application/json` (503 with
      the bundle dir empty), `/metrics` 200 `text/plain; version=0.0.4` with `nginx_*` series, `/`
      still the SPA; every access line JSON with the sent `traceparent`
- [ ] Runtime gate on the live box after deploy (post-merge; needs the lifekit-stack jobs first —
      see plan.md "Cross-repo dependency")
