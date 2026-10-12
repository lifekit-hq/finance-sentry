# Quickstart: Observability Stack

Bring the stack up and verify each user story. Adds `loki`, `prometheus`, `grafana` to the existing compose stack.

## Bring it up

```bash
cd docker
docker compose -f docker-compose.dev.yml up -d --build
# new: loki, prometheus, grafana (+ existing postgres, api, frontend, mcp)
```

## Verify by user story

**US1 — Health at a glance (P1)**
```bash
curl -s http://localhost:5001/metrics | grep finance_jobs_        # custom job metrics present
curl -s http://localhost:5001/api/v1/health/ready | jq            # Healthy + database, hangfire
# open http://localhost:3000 (Grafana) → main dashboard: API rate/latency/errors + per-job last-run
# stop api → availability panel goes red within ~60s (SC-003)
```

**US2 — Log search without SSH (P2)**
```
Grafana → Explore → Loki → {app="finance-sentry"} | level="Error", last 1h
→ app errors appear with structured fields; NO raw EF SQL at default level (FR-011)
→ search by W3C trace id groups related entries (SC-002: find an injected error in < 2 min)
```

**US3 — Job health with history (P3)**
```
# restart api, then Grafana job-health dashboard → per-job success/failure + duration trend
# survives restart because Hangfire now persists to Postgres (FR-010)
# force a job to fail twice → trend shows 2 failures with timestamps
```

**US4 — Alert on N consecutive failures (P2)**
```
# force a scheduled job to fail N consecutive times (default N=3) → ONE Telegram message
#   naming the job + consecutive count + last error (not one per failure)
# keep failing → no duplicate until a success clears the streak
# let it succeed, then fail again → re-alerts
# transient (rate-limit) failure → does NOT increment the streak
```

## Resource / retention sanity (SC-004)

```bash
docker stats                     # loki/prometheus/grafana fit the VPS envelope; p95 regression < 5% (SC-005)
# retention: Prometheus ~30d, Loki ~14d, Hangfire succeeded ~7d — bounded volumes; du plateaus
```

## Production notes

- Grafana + Hangfire dashboards reachable only over Tailscale serve (not public funnel); `/metrics` scrape-only.
- Telegram token/chat id + any dashboard secret via env/secrets, never committed.
- `/metrics` + `/health/ready` are new API surface → bump the API version + tag in the same PR.
- Grafana metric-threshold alert rules are deferred (spec Notes) until baselines exist — US4 covers the urgent silent-failure gap app-side.
