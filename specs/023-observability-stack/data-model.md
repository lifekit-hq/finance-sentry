# Phase 1 Data Model: Observability Stack

**No new application EF entities.** Persistence is delegated to Hangfire (own schema) or external stores (Loki/Prometheus); alerting reuses existing tables. FR references are to `spec.md`.

## Metric series → Prometheus (external)

- **Owner**: Prometheus TSDB; app exposes `/metrics` (OTel Prometheus exporter).
- **Series**: stock `http_server_request_duration_*` (labels: route template, method, status class), `process_*`, `dotnet_*` (FR-001); custom `finance_jobs_succeeded_total` / `finance_jobs_failed_total` / `finance_job_duration_seconds` (label: job name) and `finance_jobs_scheduled` gauge (FR-002).
- **Cardinality**: labels bounded — route templates not raw URLs, job name only, no per-user/account/PII labels (FR-007).
- **Retention**: ≥30d, hard-capped volume (FR-005/SC-004).

## Log entry → Loki (external)

- **Owner**: Loki; app only emits via the Serilog Loki sink (fire-and-forget, FR-003).
- **Shape**: timestamp, level, source module (`SourceContext`), message template + properties, W3C trace id (`TraceId`/`SpanId` body fields), exception. Labels: `app`, `module`, `level`. EF `Database.Command` SQL absent at default level (FR-011).
- **Retention**: ≥14d, capped (FR-005).

## Background job record → Hangfire (PostgreSQL, `hangfire` schema)

- **Owner**: `Hangfire.PostgreSql` (own tables/migrations; NOT an app `DbContext`).
- **Represents**: each run — job type/args, enqueue/start/finish, state (Enqueued/Processing/Succeeded/Failed/Scheduled), state history, retry count, exception. Durable across restarts (FR-010).
- **Retention**: succeeded ~7d, failed longer (config); bounded.
- **Note**: complements the app's `SyncJob` entity (domain sync bookkeeping) — Hangfire's record is the runner's own history powering dashboards + trends (US3).

## Consecutive-failure state (US4 / FR-009)

- **Represents**: per-job count of consecutive terminal failures + whether an alert already fired for the current streak.
- **Persistence**: keyed by job name; kept durably (Hangfire storage set, or a tiny keyed table) so a restart doesn't reset a live streak. Reset to 0 on the job's next `Succeeded`.
- **Consumed by**: `ConsecutiveFailureAlertFilter` — raises exactly one alert on the Nth failure, none until a success then re-failure.

## Failure Alert → existing `alerts` + `companion_events` (no migration)

- **`Alert`** (existing) gains `Type` value **`JobFailure`** (const in `AlertType`); fields already present (`UserId`, `Type`, `Severity=Error`, `Title`, `Message`, `ReferenceId`=stable job id, `ReferenceLabel`=job name). New `JobFailureSilenceWindow` in the generator backs the streak dedup.
- **`CompanionEvent`** (existing) gains appended enum **`CompanionEventKind.OperationalFailure`**; `MaterialityPolicy.ClassifyAlert("JobFailure") → OperationalFailure`. Capture → dispatch → Telegram unchanged.
- **No schema migration** (new enum/const values only).

## Dashboard → Grafana provisioning files (repo)

- **Owner**: Grafana; definitions are YAML/JSON under `docker/observability/grafana/provisioning/` (datasources + dashboards), provisioned as code so they survive container recreation (FR-004 assumption).

## Summary of code-level additions (no app DB migrations)

| Change | Location | Kind |
|---|---|---|
| `AlertType.JobFailure` | Alerts/Domain/AlertType.cs | new const |
| `GenerateJobFailureAlertAsync(...)` + silence window | Alerts/…/AlertGeneratorService.cs + Core `IAlertGeneratorService` | new method |
| `CompanionEventKind.OperationalFailure` | Companion/Domain/CompanionEventKind.cs | appended enum value |
| `"JobFailure" → OperationalFailure` | Companion/…/MaterialityPolicy.cs | new mapping |
| Custom job `Meter` + filters | Infrastructure/Observability | new metrics + Hangfire filters |
| Consecutive-failure counter | Infrastructure/Observability/Hangfire | new keyed state |
| Hangfire `hangfire` schema | PostgreSQL (Hangfire-managed) | external migration (not app EF) |
