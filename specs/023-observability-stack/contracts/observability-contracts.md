# Phase 1 Contracts: Observability Stack

Interfaces this feature exposes. FR references are to `spec.md`.

## 1. Metrics — `GET /metrics` (new, FR-001/002/006)

- Prometheus exposition text (`text/plain; version=0.0.4`); reachable only over the compose network / Tailscale, not the public funnel (FR-006).
- **200** includes stock `http_server_request_duration_*`, `process_*`, `dotnet_*` and custom `finance_jobs_succeeded_total{job}`, `finance_jobs_failed_total{job}`, `finance_job_duration_seconds{job}`, `finance_jobs_scheduled`, `finance_job_consecutive_failures{job}` (gauge: current consecutive terminal-failure streak per job, 0 after a success; transient errors excluded).
- No PII / bounded-cardinality labels (FR-007).
- Contract test: 200 + exposition contains the custom `finance_jobs_*` names; a public-funnel request is refused.

## 2. Readiness — `GET /api/v1/health/ready` (new)

- JWT-exempt; reports overall + per-dependency status.
- **200 healthy** `{ "status":"Healthy", "checks":[{"name":"database","status":"Healthy"},{"name":"hangfire","status":"Healthy"}] }`
- **503 unhealthy**: overall `Unhealthy`, failing check named (feeds SC-003 availability panel).
- Existing liveness `GET /api/v1/health` unchanged.
- Contract tests: all-up → 200 both Healthy; DB down → 503 naming `database`.

## 3. Hangfire dashboard — `GET /hangfire` (re-enabled, secured, FR-004)

- Authorized operator → 200; unauthorized → denied (never 200). Tailscale-only.
- Backed by durable Postgres storage so history/schedule persist across restarts (FR-010).
- Contract test: unauthenticated `/hangfire` is denied.

## 4. Consecutive-failure alert (internal, US4 / FR-009)

- **Trigger**: a job reaches terminal `Failed` for the **Nth consecutive** time (transient errors excluded).
- **Effect**: exactly one `Alert{Type="JobFailure"}` per streak → `ClassifyAlert → OperationalFailure` → Telegram; naming job + consecutive count + last error summary. A later `Succeeded` resets the streak so the next failure can re-alert. Dispatch is fire-and-forget (US4-AS3).
- Unit tests: (a) Nth consecutive failure → one alert with count; (b) failures 1..N-1 → no alert; (c) success resets, next failure re-alerts; (d) transient failure doesn't increment the streak; (e) dispatch throwing doesn't break the job.

## 5. Grafana dashboards + datasources (provisioned, FR-004)

- Config under `docker/observability/grafana/provisioning/`: datasources (Prometheus, Loki); dashboards — API traffic/latency/errors, per-job health (last run/duration/outcome + failure trend for US3), log search panel.
- Grafana admin auth; Tailscale-only.
- Verification (quickstart): open one dashboard → SC-001 answerable in < 30s; stop API → availability panel red within 60s (SC-003).

## 6. Log query contract (Loki via Grafana, US2 / FR-003/011)

- Logs queryable by label (`app`, `module`, `level`), correlation id, and free text; EF SQL absent at default level.
- Verification (quickstart): filter `level=Error` last 1h → app errors, no raw SQL (SC-002 ≤ 2 min to find an injected error).

## Cross-cutting

- **Versioning**: `/metrics` + `/health/ready` are new API surface → API version bump + tag in the same PR (constitution).
- **No paid dependency** anywhere (FR-008).
- **Fire-and-forget**: metrics/log/alert shipping failures never affect request or job execution (FR-003/009, SC-005).
