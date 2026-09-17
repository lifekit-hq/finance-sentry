# Feature Specification: Platform Contract Conformance (Guardrail 1, step 2)

**Feature Branch**: `fm/g1-finance-sentry-conform`

**Created**: 2026-09-17

**Status**: Implemented (pending deploy + lifekit-stack Prometheus jobs)

**Origin**: Guardrail 1 (platform contract, adopted 2026-09-16) — lifekit-stack
[PR 161](https://github.com/lifekit-hq/lifekit-stack/pull/161) shipped the checker
(`scripts/platform-contract.py`) and its contract doc (`docs/platform-contract.md`). This spec is
step 2 of that rollout: finance-sentry declares its containers and conforms on every item the
checker enforces today, and its own deploy runs the checker as a gate. There are no waivers.

## Context

The platform contract says every product on the box meets one shape: health + readiness, metrics
that reach Prometheus, JSON logs with a trace id, OTLP traces, sits behind the edge, owns its
topics. The checker enforces the items that have a platform piece behind them today —
`health`, `ready`, `metrics`, `scraped`, `logs` — and reports `traces`, `edge` and `topics` as SKIP
until the collector rule, Traefik and Redpanda land. A product declares each traffic-serving
container with `lifekit.contract.*` labels in its own compose file; datastores are `none`.

Live evidence for this product (guard-1 scout, 2026-09-16, re-checked read-only 2026-09-17 —
every container is `UNDECLARED`):

| Container | health | ready | metrics | scraped | JSON stdout + trace id |
|---|---|---|---|---|---|
| `finance-sentry-api` | `/api/v1/health` ✅ | `/api/v1/health/ready` ✅ | `/metrics` ✅ | job `finance-sentry-api` up ✅ | ❌ Serilog text console; JSON only in the Loki push |
| `finance-sentry-gateway` | `/gateway/health` ✅ | ❌ none — any other path is the proxied SPA (`200 text/html`) | `/metrics` ✅ | job `finance-sentry-gateway` up ✅ | ❌ ASP.NET text console |
| `finance-sentry-frontend` | ❌ SPA fallback answers everything | ❌ | ❌ | ❌ not on Prometheus' bridge, no job | ❌ nginx combined access log |
| `finance-sentry-mcp` | ❌ every path is `401` (JWT middleware) | ❌ | ❌ | ❌ no job | ❌ ASP.NET text console + two raw Npgsql stderr lines |
| `finance-sentry-postgres` | datastore | | | | |

Captain answers that bind this spec (2026-09-16): every traffic-serving container is `v1`; static
nginx gets `/healthz`, `/readyz` and a JSON `log_format`; the MCP gets anonymous health; JSON logs
mean **stdout** (finance-sentry switches its console to a compact JSON formatter; the Loki sink
stays until the box log agent lands); metrics means "reach Prometheus" with `/metrics` the default;
the product deploy calls the stack's checker on its own project and a missing script fails the
deploy.

---

## User Scenarios & Testing

### [US1] Every finance-sentry container is declared and the deploy gates on the declaration (P1)

The operator (Denys, or the autonomous deploy on merge) can tell from the compose file alone how
each container meets the contract, and a container with a missing or inconsistent declaration
never reaches `up`.

**Independent Test**: `docker compose -f docker/docker-compose.prod.yml config --format json |
python3 /srv/lifekit-stack/scripts/platform-contract.py --static -` prints `every service is
declared` and exits 0; removing any one `lifekit.contract.*` label from a `v1` service makes it exit 1.

**Acceptance Scenarios**:

1. **Given** the prod compose file, **When** the static gate runs, **Then** `api`, `gateway`, `mcp`
   and `frontend` are `v1` with `port`, `health`, `ready`, `metrics` and `ingress` declared, and
   `postgres` is `none`.
2. **Given** `deploy.sh`, **When** it runs, **Then** the static gate runs before `compose up` and a
   failure stops the deploy before anything changes; the script path is not guarded by an existence
   check or `|| true`, so a missing checker fails the deploy.
3. **Given** the containers are up and the api health wait has passed, **When** `deploy.sh`
   continues, **Then** it runs the runtime gate with `--project <compose project>` and a failing
   enforced item turns the deploy red.

### [US2] The gateway answers readiness from YARP's destination health (P1)

The gateway is the single front door; "up" means its upstreams are reachable, not just that the
process is listening. A readiness probe distinguishes the two.

**Independent Test**: `GET /gateway/ready` returns `200 application/json` while every cluster has
at least one available destination, `503 application/json` naming the empty cluster otherwise; it
never falls through to the SPA fallback.

**Acceptance Scenarios**:

1. **Given** every YARP cluster has at least one available destination, **When** `/gateway/ready`
   is requested, **Then** the response is `200` JSON listing each cluster with its available and
   total destination counts.
2. **Given** a cluster whose destinations are all marked unhealthy, **When** `/gateway/ready` is
   requested, **Then** the response is `503` JSON and that cluster reads `available: 0`.
3. **Given** the readiness path, **When** compared with the liveness path, **Then** they differ
   (`/gateway/health` stays the process liveness).

### [US3] The MCP host exposes anonymous liveness, readiness and metrics (P1)

The MCP is an internal tool surface behind a JWT gate. The platform probes it unauthenticated on
three paths; everything else keeps requiring a token.

**Independent Test**: without a bearer token, `GET /health` is `200 application/json`, `GET /ready`
is `200`/`503` JSON naming the `database` check, `GET /metrics` is Prometheus text; `POST /` and
any other path without a token is still `401`.

**Acceptance Scenarios**:

1. **Given** no `Authorization` header, **When** `/health` is requested, **Then** `200` JSON.
2. **Given** no `Authorization` header and a reachable database, **When** `/ready` is requested,
   **Then** `200` JSON whose `checks` include `database: Healthy`; with the database unreachable,
   `503` JSON with `database: Unhealthy`.
3. **Given** no `Authorization` header, **When** `/metrics` is requested, **Then** `200` with a
   Prometheus exposition content type.
4. **Given** no `Authorization` header, **When** any other path is requested, **Then** `401` with
   `WWW-Authenticate: Bearer`, exactly as today.

### [US4] The frontend's nginx serves health, readiness and metrics and logs JSON (P1)

Static nginx is a traffic-serving container and meets the contract itself: the SPA fallback no
longer masquerades as a health answer.

**Independent Test**: on the container's port, `/healthz` is `200 application/json`, `/readyz` is
`200` JSON while the built bundle is present, `/metrics` is `200 text/plain; version=0.0.4` with
`nginx_*` samples, and every access-log line on stdout is one JSON object carrying `traceparent`.

**Acceptance Scenarios**:

1. **Given** the frontend container, **When** `/healthz` is requested, **Then** `200`
   `application/json` — not the SPA page.
2. **Given** the built bundle is present, **When** `/readyz` is requested, **Then** `200`
   `application/json`; with `index.html` absent, `503`.
3. **Given** the frontend container, **When** `/metrics` is requested, **Then** `200` Prometheus
   text exposing nginx connection and request counters derived from `stub_status`.
4. **Given** a request carrying a `traceparent` header, **When** it is served, **Then** the stdout
   access-log line is JSON and includes that `traceparent` value.
5. **Given** a fresh container start, **When** its first 300 log lines are inspected, **Then** at
   least 90% are JSON (entrypoint chatter and startup notices are quiet).

### [US5] Every .NET host writes compact JSON to stdout with a trace id (P1)

The api, the gateway and the MCP write one JSON object per line to stdout; a line emitted inside a
traced request carries `TraceId`. The api's Loki sink keeps shipping unchanged.

**Independent Test**: `docker logs --tail 300 <container>` on each .NET container parses ≥ 90% as
JSON and at least one line carries a `TraceId` key.

**Acceptance Scenarios**:

1. **Given** an `Activity` is current, **When** a log event is written through the shared Serilog
   configuration, **Then** the console line is one JSON object with `@t`, `@m` and `TraceId`.
2. **Given** the api's `Observability:Loki:Url` is set, **When** it logs, **Then** the Loki sink
   still receives the event (no behaviour change there).
3. **Given** the MCP container starts, **When** its stdout is read, **Then** no raw Npgsql
   `Cannot load library libgssapi_krb5.so.2` lines precede the JSON lines.

### [US6] Prometheus scrapes the MCP and the frontend (P1, cross-repo)

The `scraped` item needs a Prometheus job per target. Prometheus lives in lifekit-stack and joins
the `compose_default` bridge, so this product puts the frontend on that bridge and the companion
change in lifekit-stack adds the two jobs.

**Independent Test**: `GET http://127.0.0.1:9090/api/v1/targets` on the box lists `up` targets
`finance-sentry-mcp:5100/metrics` and `finance-sentry-frontend:4200/metrics`.

**Acceptance Scenarios**:

1. **Given** the prod compose, **When** the frontend starts, **Then** it joins `compose_default`
   (alias `finance-sentry-frontend`) in addition to `finance-sentry`.
2. **Given** lifekit-stack's `prometheus.yml` carries jobs `finance-sentry-mcp` and
   `finance-sentry-frontend`, **When** both products are deployed, **Then** each target is `up`
   with samples.

### Edge Cases

- The runtime gate runs seconds after the api becomes healthy; Prometheus scrapes every 15 s, so a
  freshly recreated container's target can still read `down` from its last scrape. The deploy
  retries the runtime gate a bounded number of times before failing.
- The checker rejects `text/html` on health/ready so an SPA fallback cannot pass; every new
  endpoint here answers JSON or Prometheus text.
- `/gateway/ready` while YARP has not yet completed a first active probe: destinations of unknown
  health count as available (YARP's own semantics), so a cold gateway is ready until a probe says
  otherwise.
- The MCP's anonymous paths are exact matches (`/health`, `/ready`, `/metrics`); `/health/x` or a
  different casing still needs a token.
- The gateway proxies `/mcp/*` with the prefix stripped, so `/mcp/health`, `/mcp/ready` and
  `/mcp/metrics` become reachable through the gateway. They expose the same class of data as the
  gateway's own anonymous `/metrics`; nothing user-scoped is behind them.
- `ingress: edge` on the gateway is declared now (it publishes a loopback host port for Tailscale
  Serve); the `edge` item stays SKIP until Traefik lands, when the port goes away.

## Requirements

### Functional Requirements

- **FR-001**: `docker/docker-compose.prod.yml` MUST label `api`, `gateway`, `mcp`, `frontend` as
  `lifekit.contract: "v1"` with `port`, `health`, `ready`, `metrics`, `ingress` (and `service`
  for the OTLP name where it differs from the compose service), and `postgres` as `"none"`.
- **FR-002**: `docker/deploy.sh` MUST run the static gate (`--static -` on `compose config
  --format json`) before `compose up`, and the runtime gate (`--project <name>`) after the api
  health wait, both against `/srv/lifekit-stack/scripts/platform-contract.py` with no existence
  guard and no `|| true`.
- **FR-003**: The gateway MUST serve `GET /gateway/ready` as JSON: `200` when every YARP cluster
  has ≥ 1 available destination, `503` otherwise, listing per-cluster counts.
- **FR-004**: The MCP HTTP host MUST serve `GET /health` (JSON liveness), `GET /ready` (health
  checks tagged `ready`, at least the `database` Npgsql check, rendered by the shared readiness
  writer) and `GET /metrics` (Prometheus exporter with ASP.NET Core + runtime instrumentation,
  service name `finance-sentry-mcp`) without a bearer token; all other paths keep the JWT gate.
- **FR-005**: The frontend nginx MUST serve `/healthz`, `/readyz` (503 when `index.html` is
  missing) and `/metrics` (Prometheus text derived from `stub_status` via njs), log access lines to
  stdout as JSON with `escape=json` including `traceparent`, and keep entrypoint output and startup
  notices off stdout/stderr.
- **FR-006**: All three .NET hosts MUST write Serilog's rendered compact JSON to the console,
  enriched with `TraceId`/`SpanId` from the current `Activity`; the api's file and Loki sinks are
  unchanged.
- **FR-007**: The MCP image MUST install `krb5-libs` so Npgsql stops writing raw text to stderr.
- **FR-008**: The frontend service MUST join the external `compose_default` bridge so Prometheus
  can scrape it.
- **FR-009**: Every new HTTP endpoint MUST ship with a contract test in the same PR (constitution
  § Testing Discipline).

### Key Entities

- **Contract declaration**: the `lifekit.contract.*` label set on a compose service; the static
  gate's input, the runtime gate's probe table.
- **Readiness report** (gateway): `{ status, clusters: [{ id, available, total }] }`.
- **Readiness report** (mcp): the api's shape, `{ status, checks: [{ name, status }] }`.

## Success Criteria

### Measurable Outcomes

- **SC-001**: The static gate on the prod compose exits 0 with every service `ok` or `none`.
- **SC-002**: After the next deploy, the runtime gate on project `docker` reports `every enforced
  container passes` for `api`, `gateway`, `mcp` and `frontend` on `health`, `ready`, `metrics`,
  `scraped` and `logs` (with the lifekit-stack Prometheus jobs in place).
- **SC-003**: Zero raw text lines in the last 300 stdout lines of each .NET container after a
  fresh start; ≥ 90% JSON on the frontend.
- **SC-004**: All existing backend tests stay green; new tests cover the readiness evaluation, the
  anonymous MCP paths, the endpoint contracts and the JSON log line shape.

## Assumptions

- The checker script path on the box is `/srv/lifekit-stack/scripts/platform-contract.py`,
  readable by the `denys` runner user (verified: `-rwxr-xr-x lifekit`), and `python3` ≥ 3.9 is on
  the runner PATH (3.13 verified).
- The compose project stays named `docker` (renaming it is a separate captain host action); the
  deploy derives the name from `compose config` rather than hard-coding it.
- The Serilog major stays at 3.1.1 (the repo chose the `TraceEnricher` over a 4.x bump); the
  compact formatter package (2.0.0) targets that line, so `TraceId` is a named property, not `@tr`.
- The companion lifekit-stack change (two Prometheus jobs) is filed and deployed before this
  product's deploy runs its gate; until then the `scraped` item for `mcp` and `frontend` reads
  `no Prometheus target` and the deploy is red by design (no waivers).
- Traces, edge and topics stay SKIP; nothing here changes the OTLP exporters or Tailscale Serve.
