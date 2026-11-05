# Database Backup & Recovery

Backups are run by the API itself (Retention module, Hangfire). They are off unless configured: with no
`Backup:` settings the jobs log a warning and do nothing, so local and dev setups are safe.

## What runs

| Job (Hangfire id) | Schedule | What it does |
|---|---|---|
| `db-backup` | daily at `Backup:BackupHourUtc` (default 02:00 UTC) | `pg_dump -Fc` of the app database, encrypted with `age` to `Backup:AgeRecipient`, uploaded to S3-compatible storage under `daily/` (or `weekly/` on `Backup:WeeklyOn`, default Sunday). Then prunes to `RetainDaily` (default 30) and `RetainWeekly` (default 8) newest artifacts. |
| `db-restore-verify` | weekly | Downloads the newest artifact, decrypts it with `Backup:AgeIdentity`, `pg_restore`s it into a scratch database and runs sanity checks there; the live database is never touched. |

Artifacts are named like `daily/2026-08-06T02-00-00Z.dump.age`. `pg_dump`, `pg_restore` and `age` must be on the API container's `PATH`.

## Configuration

Bound from the `Backup:` section (environment variables `BACKUP_*` in the reference deployment, see `docker/`):

| Setting | Purpose |
|---|---|
| `Backup:R2Endpoint`, `R2Bucket`, `R2AccessKey`, `R2SecretKey` | S3-compatible target (Cloudflare R2 in the reference deployment; any S3 API works) |
| `Backup:AgeRecipient` | age **public** key the dumps are encrypted to (required for backups to run) |
| `Backup:AgeIdentity` | age **secret** key; only the restore-verify job needs it. Keep a copy off the host: without it a backup cannot be read. |

Backups run only when the endpoint, bucket, both keys and the recipient are all set.

## Restoring

1. Stop the API (`docker compose stop api`).
2. Download the artifact you want from the bucket and decrypt it:
   ```bash
   age -d -i age-identity.txt -o finance_sentry.dump daily/<artifact>.dump.age
   ```
3. Restore into an empty database:
   ```bash
   pg_restore --no-owner -d finance_sentry_restored finance_sentry.dump
   ```
4. Point the API at it, start it (pending migrations apply on boot) and spot-check recent transactions.

There is no WAL archiving or point-in-time recovery: the restore point is the artifact's timestamp.

## Encryption keys

Provider credentials are encrypted in the application (AES-256-GCM, `Encryption:Keys:<version>` with
`Encryption:CurrentKeyVersion`). The dump contains the ciphertext only, so **back the keys up separately from the
database**; a restored database without them cannot decrypt stored credentials. To rotate, add the new key version,
bump `CurrentKeyVersion`, and keep every old version until all rows are re-encrypted. Never commit keys.

Declared profile data (income, goals, career amounts) is encrypted per field with the same service and keys, into one
text column of the form `v{keyVersion}.{iv}.{tag}.{ciphertext}` (`EncryptedField`; `IsEncrypted(...)` on the property).
The key version travels with the value, and `EncryptedFieldRotationTarget<TContext>` re-seals every such column of a
context under the current key at startup, like the credential stores. Transactions and balances are **not**
field-encrypted: salary credits sit in plaintext in `transactions`, so an encrypted declared income is defence in depth
against a narrower leak (a partial dump, a log line), not a confidentiality boundary for income as a fact.

## Erasure

Do not delete rows by hand. Account closure (Settings → Danger zone) starts a grace period and the `account-purge`
job then runs every module's data eraser; see `docs/OPERATIONS_RUNBOOK.md` §12. Deleted data remains in backups until they age out under the retention above.
