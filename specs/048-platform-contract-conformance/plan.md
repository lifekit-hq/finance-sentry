# Implementation Plan: Platform Contract Conformance

**Branch**: `fm/g1-finance-sentry-conform` | **Date**: 2026-09-17 | **Spec**: [spec.md](./spec.md)

## Architecture Decisions

- **`TraceEnricher` moves to `FinanceSentry.Core.Observability`.** The gateway is deliberately
  lean (no project references) and must not pull EF/Npgsql/Hangfire in through
  `FinanceSentry.Infrastructure` just to share a 15-line enricher, and duplicating it would leave
  two definitions of "what a trace id looks like in our logs". Core is the shared kernel every host
  already depends on except the gateway, which now takes that one reference (Core carries only
  FluentResults/FluentValidation). Core gains the `Serilog` package (the abstractions library, no
  transitive dependencies). The MCP reaches Infrastructure transitively through the module
  projects, so it reuses `SerilogConfiguration` and `ReadinessResponseWriter` as they are.

- **One console format for the product: Serilog `RenderedCompactJsonFormatter`.** `@m` (rendered
  message) rather than `@mt` (template) so `docker logs` and the future log agent show readable
  text without re-rendering, and `TraceId`/`SpanId` land as named properties via the enricher
  (Serilog 3.1.1 has no native trace ids; the checker matches `traceid` case-insensitively). The
  gateway and the MCP adopt Serilog via `Serilog.AspNetCore` so the three hosts share the format
  instead of maintaining a second `ConsoleFormatter` for `Microsoft.Extensions.Logging`.
  `SerilogConfiguration` gains `For(appName)` so the MCP keeps the api's overrides, enrichers and
  file sink but tags `app=finance-sentry-mcp`; the gateway wires the same three lines inline (no
  EF override, no Loki, no file sink — it has no disk state worth keeping). The api's Loki sink is
  untouched (captain Q5: it goes when the log agent lands).

- **Gateway readiness is computed from `IProxyStateLookup`, in a pure static `GatewayReadiness`.**
  YARP already tracks per-destination health (active probe on `api`/`frontend`, passive on `mcp`)
  and exposes it as `ClusterState.DestinationsState.AvailableDestinations`. Readiness = every
  cluster has ≥ 1 available destination. Pulling the evaluation out of the endpoint lambda lets a
  unit test build `ClusterState` objects directly; a `WebApplicationFactory` contract test (active
  health checks disabled through configuration) proves the route, status code and content type.

- **MCP anonymous paths are an allow-list inside `McpJwtAuthenticationMiddleware`.** The middleware
  runs before endpoint routing, so mapping `/health` before `UseMiddleware` would not exempt it. A
  `UseWhen` split works but is only testable by booting the host; an exact-match set inside the
  middleware is one line in the existing unit-test style (`DefaultHttpContext`, no host). Exact
  match, case-sensitive: `/health`, `/ready`, `/metrics`. Everything else is unchanged.

- **MCP platform endpoints live in `McpPlatformEndpoints`** (`AddMcpPlatformEndpoints` +
  `MapMcpPlatformEndpoints`): health checks (`AddNpgSql` on `ConnectionStrings:Default`, tag
  `ready`), OpenTelemetry metrics (ASP.NET Core + runtime instrumentation, Prometheus exporter,
  resource `finance-sentry-mcp`) and the three routes. Not `OpenTelemetryConfiguration.
  AddObservabilityMetrics` from Infrastructure: that one registers the api's `JobMetrics` meter and
  the OTLP trace exporter with Npgsql/Hangfire sources, none of which the MCP has. Separate class so
  a `TestServer` can boot just these endpoints with an unreachable connection string and assert the
  `503` readiness body.

- **Frontend metrics come from `stub_status` through njs, in-container.** The checker probes
  `http://<container ip>:<port>/metrics` on the same port as health, so a sidecar exporter cannot
  satisfy it. `nginx:alpine` ships `ngx_http_js_module` and `stub_status`; a 40-line njs handler
  turns the stub text into the same series names the official nginx exporter uses
  (`nginx_connections_active`, `nginx_http_requests_total`, …). `load_module` must be in the main
  context, so the Dockerfile prepends it to the image's `nginx.conf`. The `/stub_status` location
  is `internal` (subrequest-only).

- **Frontend log noise is silenced at the source, not filtered.** The 90%-JSON rule is evaluated
  on the last 300 lines seconds after a recreate, when a container has emitted maybe ten lines. The
  nginx entrypoint's `/docker-entrypoint.sh: …` lines go quiet with `NGINX_ENTRYPOINT_QUIET_LOGS=1`
  and the `[notice] start worker process` lines drop by lowering `error_log` to `warn`. Same reason
  the MCP image installs `krb5-libs`: Npgsql writes two raw stderr lines when it cannot `dlopen`
  the GSSAPI library, and on a fresh container those two lines alone break the ratio (the api image
  already installs it for the same reason).

- **Deploy gate: static before `up`, runtime after the health wait with a bounded retry.** Same
  shape as lifekit-stack's `deploy.sh` and the contract doc's product snippet. The runtime gate
  retries up to 4 times, 15 s apart (one Prometheus scrape interval), because the api health wait
  returns the moment the api answers and the `scraped` item reads the *last* scrape, which for a
  container recreated seconds ago is still `down`. The last failure exits 1: containers stay up
  (post-deploy assertion model, captain Q2), the job goes red.

- **Frontend joins `compose_default`.** Prometheus only joins that bridge; adding the frontend to it
  is a compose-level change here, whereas making Prometheus join `finance-sentry` would be a
  lifekit-stack change for one product. The frontend serves a public SPA bundle; nothing on that
  bridge gains access it did not already have through the gateway.

## Cross-repo dependency

lifekit-stack `compose/observability/prometheus/prometheus.yml` needs two jobs (companion PR,
same shape as the existing `finance-sentry-gateway` job):

```yaml
  - job_name: finance-sentry-mcp
    metrics_path: /metrics
    static_configs:
      - targets: ['finance-sentry-mcp:5100']

  - job_name: finance-sentry-frontend
    metrics_path: /metrics
    static_configs:
      - targets: ['finance-sentry-frontend:4200']
```

Order: the stack change deploys first (the two targets read `down` until this PR deploys — harmless,
no `up == 0` alert rule exists), then this PR merges and its deploy gate goes green.

## Files

| Area | Path | Change |
|---|---|---|
| Core | `backend/src/FinanceSentry.Core/Observability/TraceEnricher.cs` | moved from Infrastructure (+ `Serilog` package) |
| Infrastructure | `Observability/SerilogConfiguration.cs` | JSON console, `For(appName)` |
| Gateway | `Program.cs`, `GatewayReadiness.cs`, `.csproj` | Serilog JSON console, `/gateway/ready`, Core ref |
| MCP | `Program.cs`, `McpPlatformEndpoints.cs`, `Middleware/McpJwtAuthenticationMiddleware.cs`, `.csproj` | Serilog, anonymous `/health` `/ready` `/metrics` |
| Docker | `docker-compose.prod.yml`, `deploy.sh`, `Dockerfile.mcp`, `Dockerfile.frontend.prod`, `nginx.frontend.conf`, `nginx.metrics.js` | labels, gates, krb5, njs metrics, JSON access log |
| Tests | `Gateway.Tests/GatewayReadinessTests.cs`, `Gateway.Tests/GatewayEndpointContractTests.cs`, `Mcp.Tests/McpJwtAuthenticationMiddlewareTests.cs`, `Mcp.Tests/McpPlatformEndpointsTests.cs`, `Tests.Unit/Observability/JsonConsoleLogLineTests.cs` | new + extended |
| Docs | `docs/claude/app-state.md`, this spec folder | state block |
