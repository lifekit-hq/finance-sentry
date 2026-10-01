# Operations Runbook — Bank Sync

## 1. Sync Failing for an Account

**Symptoms:** `SyncJob.Status = 'failed'`, `BankAccount.SyncStatus = 'failed'`

**Steps:**
1. Check `sync_jobs` table for `error_message` and `error_code`.
2. If `error_code = 'MONOBANK_RATE_LIMITED'` / `RATE_LIMIT_EXCEEDED`: the provider is throttling. Wait and let the next scheduled cycle retry.
3. If `error_code = 'ITEM_LOGIN_REQUIRED'`: User must re-link (expired TrueLayer consent or revoked Monobank token). Account status = `reauth_required`. Notify user.
4. If `error_code = 'DATABASE_ERROR'`: Check DB connectivity. Run `SELECT 1` against PostgreSQL.
5. Manually trigger re-sync once root cause resolved:
   ```
   POST /api/accounts/{accountId}/sync?userId={userId}
   ```

## 2. High Error Rate (5xx)

**Symptoms:** Grafana alert, error rate > 1%

**Steps:**
1. Check Serilog/Application Insights for ERROR-level logs.
2. Common causes:
   - DB connection pool exhausted: increase `Max Pool Size` in connection string.
   - Provider API down: check TrueLayer / Monobank status pages. Affected syncs will fail until resolved.
   - Memory pressure: restart the API container (`docker-compose restart api`). Prod containers carry `mem_limit` caps (values and 7-day-peak rationale live beside each service in `docker/docker-compose.prod.yml`); a container that keeps restarting with `OOMKilled=true` (`docker inspect <name> --format '{{.State.OOMKilled}}'`) has hit its cap — raise the cap there rather than restarting repeatedly.
3. If DB migration pending: run `dotnet ef database update`.

## 3. Slow Queries (> 100ms)

**Symptoms:** `EFQueryLoggerInterceptor` WARNING logs

**Steps:**
1. Identify slow query in logs (`SLOW QUERY [XXXms] SQL: ...`).
2. Run `EXPLAIN ANALYZE` on the query in PostgreSQL.
3. Common fixes:
   - Missing index: check `idx_transaction_account_active`, `idx_bank_account_user_id`.
   - N+1: use `Include()` / `Join()` instead of looping queries.
4. If N+1 suspected: look for `Potential N+1 detected: X DB round-trips` in logs.

## 4. Hangfire Jobs Not Running

**Symptoms:** Recurring jobs not appearing in Hangfire Dashboard

**Dashboard access:** in every environment (Development included) the dashboard serves only a signed-in account holding the
`ops.admin` permission (Owner role; policy `RequireOwner`); anyone else gets 401 (not signed in) or 403 (signed in,
without it) — there is no read-only view. To open it, sign in to the app, then go to `/hangfire` on
the same origin (`https://finance-sentry.<tailnet>.ts.net:4200/hangfire` or `:5001/hangfire`) — the
access-token cookie is sent with the navigation. On a 401 after a long break, reload the app once to
refresh the token and retry.

Who holds the role: at startup the API grants `Owner` to the account named by `Auth__OwnerEmail`
(prod: `AUTH_OWNER_EMAIL` in the deploy env; not a secret). With it unset, the role goes to the only
registered account, and only while nobody holds it — so once a second account exists, set
`AUTH_OWNER_EMAIL` explicitly. The seed only ever adds the role; to take it away, delete the row from
`auth."AspNetUserRoles"`. The API host rebuilds the principal from the account's current roles on every request
(`AccessTokenPrincipalLoader`, no cache), so a role change, lockout or People-page revoke applies to the next request.

**Steps:**
1. Navigate to `/hangfire` in browser (see *Dashboard access* above).
2. Check server list — if empty, Hangfire worker is not running. Restart API.
3. Check `SyncScheduler.ScheduleAllActiveAccounts()` was called at startup (logged at startup).
4. Re-register recurring jobs: restart the API (scheduler runs on startup).
5. Recurring jobs register in the background once the API is listening, so a held Hangfire lock no longer delays or crashes startup. A process killed while holding `hangfire:lock:recurring-job:<id>` leaves the lock row until Hangfire.PostgreSql's `DistributedLockTimeout` (10 min default) passes; registration retries through that window. `Recurring job registration hit a Hangfire lock timeout (attempt n)` warnings and a `job-registration` readiness check still `Healthy` ("in progress") need no action. If the log shows `Recurring job registration gave up` and `/api/v1/health/ready` reports `job-registration` Unhealthy, the retry budget (storage lock timeout + 2 min) was spent: the API stays up with the jobs already in storage but this build's schedule changes are not applied — restart the API once the other lock holder is gone. Any other exception during registration stops the host, as it did when registration ran inline at startup.

## 5. Audit Log Table Growing Too Large

**Symptoms:** `audit_logs` table > 10GB

**Steps:**
1. Archive old rows (> 1 year) to cold storage:
   ```sql
   DELETE FROM audit_logs WHERE performed_at < NOW() - INTERVAL '1 year';
   ```
2. Add a partition by month if volume is sustained (consult DBA).

## 6. Data Retention Job Failing

**Symptoms:** `DataRetentionJob` ERROR in logs

**Steps:**
1. Check Hangfire Dashboard for job error details.
2. Common cause: DB connection issue. Fix DB, then re-trigger manually:
   ```csharp
   backgroundJobClient.Enqueue<DataRetentionJob>(j => j.RunAsync(false, CancellationToken.None));
   ```
3. Use dry-run mode first to verify: `j.RunAsync(true, ...)` — logs count without archiving.

## 7. Health Check Returning Unhealthy

```
GET /health/ready → 503
```

**Steps:**
1. Check response body: `{ "status": "Unhealthy", "checks": [ { "name": "database", "status": "Unhealthy" }, ... ] }` — each check is named. A check that fails with a plain message carries it as `description` (`migrations` naming the pending migrations; `hangfire` when its storage is reachable but no server is registered). A check that fails with an exception — `database`, or `hangfire` when its storage is unreachable — carries name and status only: the endpoint is unauthenticated, so the failure detail stays in the API log, not the response.
2. If `database` unhealthy: PostgreSQL is unreachable. Check Docker container status.
3. If still failing after DB restart: check connection string in `appsettings.json`.
4. If `migrations` unhealthy: the database was unreachable when the API started, so startup skipped module migrations and the API is serving a schema that is behind. The `description` lists the pending migrations per module (or just says the database is still unreachable — then step 2 applies first). Startup also skipped registering this build's Hangfire jobs (they write to the same database). Restart the API once the database is reachable; migrations then run as usual (§8 if one fails) and the jobs are registered. Until then, requests against the missing schema fail with ordinary database errors that never mention migrations — this check is where that cause is named.

## 8. Startup Migration Failure

**Symptoms:** `deploy.sh` fails with `STARTUP MIGRATION FAILURE`, or the api container crash-loops and `docker compose -f docker/docker-compose.prod.yml logs api | grep StartupMigrationException` matches.

**Meaning:** a module's EF Core migration failed against a reachable database (or the database connection was lost after earlier modules had migrated), and the API refused to start rather than serve a half-migrated schema.

**Steps:**
1. Find the `STARTUP MIGRATION FAILURE: <Context> could not apply migration <Migration>` log line — it names the module's `DbContext` and the migration; the attached `StartupMigrationException` inner exception is the underlying database error.
2. Fix the underlying migration issue (or restore database connectivity) and ship a new commit.
3. Migrations are idempotent: the next start resumes from the failed migration rather than re-running earlier ones.

## Contact

- On-call channel: `#finance-sentry-oncall`
- Escalation: DBA team for database issues; TrueLayer / Monobank support for provider API issues.
