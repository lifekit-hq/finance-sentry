# Tasks — Spec 048: Platform Contract Conformance

## [US5] JSON stdout with trace id (shared plumbing first — US2/US3 build on it)

- [ ] T001 Move `TraceEnricher` to `FinanceSentry.Core.Observability`; add `Serilog` to Core; fix
      the Infrastructure and unit-test usings
- [ ] T002 `Serilog.Formatting.Compact` 2.0.0 into `Directory.Packages.props` + Infrastructure;
      `SerilogConfiguration` writes `RenderedCompactJsonFormatter` to the console and gains
      `For(appName)`
- [ ] T003 `JsonConsoleLogLineTests` — a line written through the compact formatter with the
      enricher inside an `Activity` parses as JSON with `@t`, `@m`, `TraceId`, `SpanId`; outside an
      `Activity` it has no `TraceId`

## [US2] Gateway readiness

- [ ] T004 `GatewayReadiness.Evaluate(IEnumerable<ClusterState>)` → `GatewayReadinessReport`
- [ ] T005 `Program.cs` — Serilog JSON console (`Serilog.AspNetCore`, Core reference), map
      `GET /gateway/ready` (200/503 JSON)
- [ ] T006 `GatewayReadinessTests` — all clusters available → ready; one empty cluster → not
      ready, that cluster reads `available: 0`; no clusters → not ready
- [ ] T007 `GatewayEndpointContractTests` (`WebApplicationFactory<Program>`, active health checks
      off, OTLP off) — `/gateway/health` 200 JSON, `/gateway/ready` 200 JSON with every configured
      cluster, `/metrics` 200 Prometheus text

## [US3] MCP anonymous platform endpoints

- [ ] T008 `McpJwtAuthenticationMiddleware` — exact-match anonymous set `/health`, `/ready`,
      `/metrics`
- [ ] T009 `McpPlatformEndpoints` — `AddMcpPlatformEndpoints` (Npgsql `database` check tagged
      `ready`; OTel metrics + Prometheus exporter, service `finance-sentry-mcp`) and
      `MapMcpPlatformEndpoints` (`/health`, `/ready` via `ReadinessResponseWriter`, `/metrics`)
- [ ] T010 `Program.cs` http branch — `UseSerilog(SerilogConfiguration.For("finance-sentry-mcp"))`,
      request logging, platform endpoints
- [ ] T011 `Dockerfile.mcp` — `apk add --no-cache krb5-libs`
- [ ] T012 `McpJwtAuthenticationMiddlewareTests` — anonymous paths pass through without a token;
      `/health/x` and `/HEALTH` still 401
- [ ] T013 `McpPlatformEndpointsTests` (`TestServer`) — `/health` 200 JSON; `/ready` with an
      unreachable connection string 503 JSON naming `database: Unhealthy`; `/metrics` 200
      Prometheus content type

## [US4] Frontend nginx

- [ ] T014 `nginx.frontend.conf` — JSON `log_format` with `traceparent`, `access_log /dev/stdout`,
      `/healthz`, `/readyz`, `/metrics` (njs), internal `/stub_status`
- [ ] T015 `nginx.metrics.js` — `stub_status` → Prometheus exposition
- [ ] T016 `Dockerfile.frontend.prod` — `load_module` + `error_log warn` in `nginx.conf`, copy the
      njs file, `NGINX_ENTRYPOINT_QUIET_LOGS=1`

## [US1] + [US6] Compose declaration and deploy gate

- [ ] T017 `docker-compose.prod.yml` — `lifekit.contract.*` labels on all five services; frontend
      joins `openclaw` (`compose_default`)
- [ ] T018 `deploy.sh` — static gate before `up`; runtime gate after the health wait with a bounded
      retry
- [ ] T019 `docs/claude/app-state.md` — platform-contract block; spec status

## Verification

- [ ] `dotnet build backend/FinanceSentry.sln -m:1` — 0 warnings
- [ ] `dotnet test backend/FinanceSentry.sln --no-build -m:1` — green
- [ ] Static gate on the prod compose: `every service is declared`
- [ ] Scratch `nginx:alpine` container with the new conf + njs (same `sed`s as the Dockerfile):
      `/healthz`, `/readyz`, `/metrics` content types; JSON access line with `traceparent`
- [ ] Runtime gate on the live box after deploy (post-merge; needs the lifekit-stack jobs)
