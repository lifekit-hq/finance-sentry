# Phase 0 Research: Data Retention & Backups

**Feature**: 024-data-retention | **Date**: 2026-08-06

All spec assumptions and open plan-time choices are resolved below. No `NEEDS CLARIFICATION` remain.

---

## D1. Retention policy registry — form & location

**Decision**: A strongly-typed, static C# registry (`RetentionPolicyRegistry`) inside a new
`FinanceSentry.Modules.Retention` module. Every persistent table in the system appears
exactly once as a `RetentionPolicy` record: `{ Schema, Table, TimestampColumn, Action
(Purge|Downsample|Keep), WindowDays, BatchSize, EnforcedBy }`.

**Rationale**:
- Spec `[DECISION]` mandates the registry lives in the repo (reviewable, versioned), not in DB config. A compiled C# list is versioned, greppable, and unit-testable.
- A **coverage guard test** reflects over every registered `DbContext`'s entity→table mappings and asserts each table has a registry entry. This enforces the edge case "a table added by a future feature without a policy must be surfaced" (FR-001) — a new table with no policy fails CI.
- `EnforcedBy` records whether the generic engine or a pre-existing bespoke job enforces the policy (see D2), so the "single source of truth" guarantee holds even where enforcement is distributed.

**Alternatives considered**:
- YAML/JSON registry file — loses compile-time coupling to real table names; the coverage guard test becomes stringly-typed and drifts. Rejected.
- Per-module policy attributes on entities — scatters the registry, defeating "single reviewable registry." Rejected.

---

## D2. Enforcement: generic engine + existing bespoke jobs (no rip-and-replace)

**Decision**: The registry is the single source of truth for *decisions*. Enforcement is split, recorded per-policy via `EnforcedBy`:
- **Generic `RetentionPurgeJob`** enforces every `Action=Purge` policy where `EnforcedBy=Generic` — currently-unmanaged growing tables: `bank_sync.sync_jobs`, `research.candidate_scores`, `research.valuation_snapshots`, `research.analyst_actions` (observational rows only), `risk.holding_snapshots`, `companion.companion_events`, `analytics.query_audit`, `research.macro_events` (past events), `research.recommendation_trends` (superseded periods).
- **Existing bespoke jobs stay** and their tables are listed with `EnforcedBy=<job name>` (documentation + coverage only, generic engine skips them): `bank_sync.transactions` (soft-archive `DataRetentionJob`), `alerts.alerts` (`AlertPurgeJob`), `radar.radar_signals` (`RadarComputeJob` prune), `research.opportunity_candidates` (`CandidateExpiryJob`).

**Rationale**: Ship-first — do not destabilize four working, tested purge paths. The registry + coverage test still deliver FR-001's "every table has an explicit decision." Consolidating the bespoke jobs into the engine is a later refactor, not a launch blocker.

**Alternatives considered**: Migrate all bespoke purges into the generic engine now — larger blast radius (transaction soft-archive semantics differ from hard-delete; radar prune is coupled to compute). Rejected for launch; noted as follow-up.

---

## D3. Batched, idempotent purge mechanics

**Decision**: Generic purge deletes in bounded batches via raw parameterized SQL executed
on the shared connection, scoped by `schema.table` + `timestamp_column < @cutoff`, looping
`DELETE FROM {schema}.{table} WHERE ctid IN (SELECT ctid FROM {schema}.{table} WHERE
{ts} < @cutoff LIMIT @batch)` until affected-rows = 0. Cutoff computed in **UTC**.

**Rationale**:
- Repo idiom is `ExecuteDeleteAsync`, but that spans one `DbContext`; the generic engine crosses 13 schemas, so raw batched SQL on the connection is the least-coupled fit and avoids loading rows into memory (the "years of backlog" edge case).
- `ctid`+`LIMIT` batching bounds lock duration and lets a killed run resume with no double-delete (idempotent — cutoff is far from `now()`, so concurrent sync writes are never in range). Satisfies US1-AS3 and the deadlock edge case.
- Table/schema identifiers come only from the compiled registry (never user input) → no SQL-injection surface; still quoted via `"`.

**Alternatives considered**: `ExecuteDeleteAsync` per module context — needs 13 context injections and can't batch a single statement without extra plumbing. Rejected for the generic path.

---

## D4. Downsampling (US3, P2)

**Decision**: A `DownsampleJob` driven by per-table `IDownsampler` implementations, one per
downsample policy. Launch scope: **defer to P2, after US1/US2 land.** Two candidates:
- `radar.daily_bars` → beyond ~365d, collapse to one weekly bar (OHLC: first open, max high, min low, last close, summed volume) per `(Ticker, week)`; delete the dailies, insert the weekly aggregate transactionally.
- `wealth.net_worth_snapshots` → beyond ~365d, keep one snapshot per ISO week per user (the last of each week), delete the rest.

**Rationale**: Aggregation is table-specific and must preserve chart continuity (US3-AS2) — a plug-per-table interface keeps each correct and independently testable. Only two tables genuinely need it, so a generic aggregation DSL is over-engineering. Marked P2 because plain purge (US1) + backups (US2) deliver the core "stops growing" + "recoverable" value.

**Alternatives considered**: Generic SQL `date_trunc` rollup — can't express OHLC semantics correctly. Rejected.

---

## D5. Retention & backup run records

**Decision**: New `RetentionDbContext` (schema `retention`, history table
`__ef_migrations_history_retention`) with two tables:
- `retention_runs` — `{ Id, RunType (Purge|Downsample), StartedAt, CompletedAt, Outcome (Success|PartialSuccess|Failed), TableResults jsonb (table → {examined, removed}), Error? }`.
- `backup_runs` — `{ Id, CreatedAt, Kind (Backup|RestoreVerify), ArtifactKey, SizeBytes, Sha256, Encrypted, VerificationStatus (Pending|Verified|Failed), VerifiedAt?, Error? }`.

**Rationale**: US1-AS2 wants operator-reviewable per-table rows-examined/removed + duration — nothing structured exists today (current jobs only log to Serilog). A dedicated context mirrors the established pattern (023 Hangfire schema, 031 CompanionDbContext). `TableResults` as jsonb avoids a child table for a low-cardinality breakdown. SC-002 ("last provably-restorable backup in <1 min") = `SELECT max(VerifiedAt) FROM retention.backup_runs WHERE VerificationStatus='Verified'`, also surfaced as a Prometheus gauge.

**Alternatives considered**: Reuse Hangfire-storage hashes (as `JobFailureStreakStore` does) — not queryable/joinable for the operator review story. Serilog-only — not structured enough for SC-002/US1-AS2. Rejected.

---

## D6. Backup: pg_dump + age + Cloudflare R2 (in-app Hangfire job)

**Decision** (per user choice): `BackupJob` (nightly `Cron.Daily(hourUtc)`), `[AutomaticRetry(Attempts=0)]`:
1. `pg_dump -Fc` (custom format, compressed) of the app database, streamed —
2. through `age --encrypt --recipients-file` (dedicated backup age recipient) —
3. multipart-uploaded to a Cloudflare R2 bucket via `AWSSDK.S3` (R2 speaks the S3 API; endpoint + keys from `.env.sops`).
4. Write a `backup_runs` row (`VerificationStatus=Pending`); prune R2 objects beyond the retention window (keep 30 dailies + 8 weeklies via object key prefixes `daily/` `weekly/`).

`RestoreVerifyJob` (weekly `Cron.Weekly()`), `[AutomaticRetry(Attempts=0)]`:
1. Download the latest `Pending`/most-recent backup from R2 → `age --decrypt` →
2. `createdb restore_verify_<utcstamp>` on the same Postgres server → `pg_restore` into it →
3. Read-only verification against the scratch DB: table row counts sane, latest `bank_sync.transactions.posted_date` within expected recency →
4. `dropdb` the scratch DB; update the `backup_runs` row to `Verified`/`Failed` + `VerifiedAt`.

**Tooling in the API image**: add `postgresql-client-14` (version-matched `pg_dump`/`pg_restore`/`createdb`/`dropdb`) and the `age` binary to the API Dockerfile.

**Encryption keys**: a dedicated backup age keypair (not the SOPS key) — `BACKUP_AGE_RECIPIENT` (public, for encrypt) + `BACKUP_AGE_IDENTITY` (private, for restore-verify decrypt), both in `docker/.env.sops`.

**Rationale**:
- R2: 10 GB free tier, zero egress, always-online, truly off-host (FR-005). We encrypt with age *before* upload, so R2's own at-rest encryption is belt-and-suspenders and a bucket leak yields ciphertext.
- In-app Hangfire (user choice): backup/restore runs inherit the exact scheduling, `retention_runs`/`backup_runs` recording, `JobMetricsFilter` success/fail/duration, and `ConsecutiveFailureAlertFilter → Alerts → Companion → Telegram` path the retention jobs use (FR-007) with zero extra wiring. Cost: `postgresql-client` + `age` in the image, and backups skip if the app is down (accepted — daily cadence + failure alerting catch a persistent outage).
- Restore isolation: a uniquely-named scratch **database** on the same server, restored into and dropped, verified read-only — `pg_restore` targets only that DB, so production tables are never written (edge case + FR-006). Simpler and lighter than spinning an ephemeral Postgres container from inside a job.

**Alternatives considered**:
- Dedicated backup sidecar container — cleaner separation, survives app downtime, but re-implements outcome surfacing into Prometheus/Loki/Telegram. Rejected per user choice.
- `pg_basebackup`/WAL archiving / PITR — spec marks PITR out of scope; nightly logical dumps meet the single-user RPO. Rejected.
- rclone instead of `AWSSDK.S3` — another binary + config file; the S3 SDK keeps upload/prune in-process and testable. Chose SDK.

---

## D7. Log & job-history capping (FR-004)

**Decision**:
- **Serilog file sink**: `rollingInterval: Day`, `retainedFileCountLimit: 14`, `fileSizeLimitBytes: 100 MB`, `rollOnFileSizeLimit: true` on the `api_logs` volume (add caps if the file sink lacks them; console/Loki sinks unchanged).
- **Loki**: already bounded (compactor + retention) — verify 30d retention is on.
- **Prometheus**: already `--storage.tsdb.retention.time=30d --storage.tsdb.retention.size=5GB` — no change.
- **Hangfire history**: rely on Hangfire's built-in succeeded-job expiration; set `JobExpirationTimeout` explicitly (e.g. 3 days) in `HangfireSetup` so the `hangfire` schema stays bounded.

**Rationale**: Most of FR-004 is already satisfied by 023's Loki/Prometheus retention. The only gaps are explicit Serilog file caps and a pinned Hangfire expiration. No new infra.

---

## D8. Observability integration (FR-007)

**Decision**: Because every retention/backup job is a Hangfire job, `JobMetricsFilter`
already records `finance_jobs_succeeded/_failed/_duration` and
`ConsecutiveFailureAlertFilter` already escalates repeated failures to Telegram — free.
Add two observable gauges to `JobMetrics`:
- `finance_backup_last_verified_age_seconds` (from `max(VerifiedAt)` in `backup_runs`) → powers SC-002.
- `finance_retention_last_run_age_seconds{run_type}`.
Add a Grafana dashboard `docker/observability/grafana/dashboards/retention-backups.json`
(backup age, last-verified timestamp, rows-removed-per-run, restore-drill status) — dashboards-as-code, matching 023.

**Rationale**: Reuse the 023 rails end-to-end; the only net-new is two gauges + one dashboard. No REST endpoints needed — operator visibility is Grafana + the two DB tables, so no new contract-test surface (keeps the change backend-internal).

---

## D9. Initial policy windows (proposals from spec Assumptions, tuned)

| Table | Action | Window | Enforced by |
|---|---|---|---|
| `bank_sync.sync_jobs` | Purge | 90d | Generic |
| `analytics.query_audit` | Purge | 180d | Generic |
| `companion.companion_events` | Purge | 90d | Generic |
| `research.candidate_scores` | Purge | 180d | Generic |
| `research.valuation_snapshots` | Purge | 365d | Generic |
| `research.macro_events` | Purge (past only) | 365d | Generic |
| `research.recommendation_trends` | Purge (superseded periods) | 365d | Generic |
| `risk.holding_snapshots` | Purge | 180d | Generic |
| `bank_sync.transactions` | Keep (soft-archive 24m) | — | `DataRetentionJob` |
| `alerts.alerts` | Purge (resolved/dismissed) | 90d | `AlertPurgeJob` |
| `radar.radar_signals` | Purge (info severity) | 730d | `RadarComputeJob` |
| `research.opportunity_candidates` | Purge (expired) | — | `CandidateExpiryJob` |
| `radar.daily_bars` | Downsample → weekly | 365d | `DownsampleJob` (P2) |
| `wealth.net_worth_snapshots` | Downsample → weekly | 365d | `DownsampleJob` (P2) |
| transactions, bank_accounts, theses, watchlist, IPS, budgets, holdings, credentials, connections, rule_sets, subscriptions, RAG corpus | Keep forever | — | (registry `Keep`) |

Windows are config-overridable via `RetentionOptions` (bound from config) so they can be tuned against real VPS table sizes without a redeploy of the registry defaults. FR-008 honored: only observational/derived data is purged; user-initiated financial records are `Keep`.

**Migration-history-table discrepancy noted** (from codebase map): Budgets/Subscriptions/Alerts and CryptoSync/BrokerageSync have mismatched history-table names between runtime module registration and design-time factories. This does **not** affect logical `pg_dump`/`pg_restore` (which dump data + schema regardless of the history table used at build time) but is logged as a **follow-up cleanup** so a future physical-restore or per-schema migration path isn't tripped up. Out of scope for 024 delivery.
