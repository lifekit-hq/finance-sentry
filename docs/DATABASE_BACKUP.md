# Database Backup & Recovery

Backups are run by the API itself (Retention module, Hangfire). They are off unless configured: with no
`Backup:` settings the jobs log a warning and do nothing, so local and dev setups are safe.

## What runs

| Job (Hangfire id) | Schedule | What it does |
|---|---|---|
| `db-backup` | daily at `Backup:BackupHourUtc` (default 02:00 UTC) | `pg_dump -Fc` of the app database, encrypted with `age` to `Backup:AgeRecipient`, uploaded to S3-compatible storage under `daily/` (or `weekly/` on `Backup:WeeklyOn`, default Sunday). Then prunes to `RetainDaily` (default 30) and `RetainWeekly` (default 8) newest artifacts. |
| `db-restore-verify` | weekly (Hangfire `Cron.Weekly()`: Sunday 00:00 UTC) | Downloads the newest artifact under `daily/` or `weekly/`, decrypts it with `Backup:AgeIdentity`, `pg_restore`s it into a scratch database named `restore_verify_<timestamp>`, checks that it holds at least 20 tables and that `bank_sync."Transactions"` is readable, then drops the scratch database; the live database is never touched. |

Artifacts are named like `daily/2026-08-06T02-00-00Z.dump.age`. `pg_dump`, `pg_restore`, `createdb`, `dropdb` and `age` must be on the API container's `PATH`; the API image installs `postgresql16-client` and `age` (`docker/Dockerfile`). Each run is recorded in `retention.backup_runs` (artifact key, size, SHA-256, verification status, error); a failed backup or drill is recorded as `Failed` and the job rethrows, so it shows as a failed Hangfire job.

## Monitoring

`finance_backup_last_verified_age_seconds` and `finance_backup_last_verified_timestamp` report the last backup a drill proved restorable; the Grafana dashboard `retention-backups` (`docker/observability/grafana/provisioning/dashboards/`) charts both.

## Configuration

Bound from the `Backup:` section (environment variables `BACKUP_*` in the reference deployment, see `docker/`):

| Setting | Purpose |
|---|---|
| `Backup:R2Endpoint`, `R2Bucket`, `R2AccessKey`, `R2SecretKey` | S3-compatible target (Cloudflare R2 in the reference deployment; any S3 API works) |
| `Backup:AgeRecipient` | age **public** key the dumps are encrypted to (required for backups to run) |
| `Backup:AgeIdentity` | age **secret** key; only the restore-verify job needs it, and without it the weekly drill fails. Keep a copy off the host: without it a backup cannot be read. |

Backups run only when the endpoint, bucket, both keys and the recipient are all set.

## Restoring

1. Stop the API (`docker compose stop api`).
2. Download the artifact you want from the bucket and decrypt it:
   ```bash
   age -d -i age-identity.txt -o finance_sentry.dump daily/<artifact>.dump.age
   ```
3. Create an empty database and restore into it (the same flags the weekly drill uses):
   ```bash
   createdb finance_sentry_restored
   pg_restore --no-owner --no-privileges -d finance_sentry_restored finance_sentry.dump
   ```
4. Point the API at it, start it (pending migrations apply on boot) and spot-check recent transactions.

There is no WAL archiving or point-in-time recovery: the restore point is the artifact's timestamp.

## Encryption keys

Provider credentials are encrypted in the application (AES-256-GCM, `Encryption:Keys:<version>` with
`Encryption:CurrentKeyVersion`). The dump contains the ciphertext only, so **back the keys up separately from the
database**; a restored database without them cannot decrypt stored credentials. To rotate, add the new key version,
bump `CurrentKeyVersion`, and keep every old version until all rows are re-encrypted. Never commit keys.

Declared profile data (the label and amount of each income source and career event, plus a career event's note, in the Wealth module) is encrypted per field with the same service and keys, into one
text column of the form `v{keyVersion}.{iv}.{tag}.{ciphertext}` (`EncryptedField`; `IsEncrypted(...)` on the property).
The key version travels with the value, and `EncryptedFieldRotationTarget<TContext>` re-seals every such column of a
context under the current key at startup, like the credential stores (Wealth registers one for its context). Transactions and balances are **not**
field-encrypted: salary credits sit in plaintext in `transactions`, so an encrypted declared income is defence in depth
against a narrower leak (a partial dump, a log line), not a confidentiality boundary for income as a fact.

## Erasure

Do not delete rows by hand. Account closure (Settings → Danger zone) starts a grace period and the `account-purge`
job then runs every module's data eraser; see `docs/OPERATIONS_RUNBOOK.md` §12. Deleted data remains in backups until they age out under the retention above (with the defaults, up to 8 weeks for the weekly set).
