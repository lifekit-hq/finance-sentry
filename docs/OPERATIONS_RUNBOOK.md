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
   - Memory pressure: restart the API container (`docker-compose restart api`).
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

**Steps:**
1. Navigate to `/hangfire` in browser.
2. Check server list — if empty, Hangfire worker is not running. Restart API.
3. Check `SyncScheduler.ScheduleAllActiveAccounts()` was called at startup (logged at startup).
4. Re-register recurring jobs: restart the API (scheduler runs on startup).

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
1. Check response body: `{ "status": "Unhealthy", "checks": { "npgsql": "Unhealthy" } }`
2. If `npgsql` unhealthy: PostgreSQL is unreachable. Check Docker container status.
3. If still failing after DB restart: check connection string in `appsettings.json`.

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
