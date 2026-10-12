# Database Backup & Recovery

## Backup Schedule

| Type | Frequency | Retention | Tool |
|------|-----------|-----------|------|
| Full | Daily 02:00 UTC | 30 days | pg_dump |
| WAL  | Continuous | 7 days | PostgreSQL WAL archiving |
| Weekly snapshot | Sunday 03:00 UTC | 12 weeks | pg_dump + S3 |

## Automated Backup (pg_dump)

```bash
pg_dump -Fc \
  -h $DB_HOST -U $DB_USER -d finance_sentry \
  -f /backups/finance_sentry_$(date +%Y%m%d_%H%M%S).dump
```

Encrypt before uploading:

```bash
openssl enc -aes-256-cbc -pbkdf2 \
  -in finance_sentry.dump \
  -out finance_sentry.dump.enc \
  -pass env:BACKUP_ENCRYPTION_KEY
```

## Point-In-Time Recovery

1. Stop the application (`docker-compose stop api`)
2. Restore the nearest full backup:
   ```bash
   pg_restore -Fc -d finance_sentry_restored finance_sentry_YYYYMMDD.dump
   ```
3. Apply WAL logs up to the target timestamp:
   ```
   restore_command = 'aws s3 cp s3://finance-sentry-wal/%f %p'
   recovery_target_time = '2026-03-28 15:30:00+00'
   ```
4. Verify row counts and spot-check recent transactions.
5. Restart the application.

## Encryption Key Backup

The AES-256-GCM master key (`Deduplication:MasterKeyBase64`) must be backed up separately from the database:

- Store in AWS Secrets Manager or HashiCorp Vault.
- Key rotation: generate a new key, re-encrypt all `encrypted_credentials` rows, update the secret. Never delete the old key version until all rows are migrated.
- **Never commit keys to git.**

## Recovery Testing

Run a full restore drill monthly:

1. Restore latest backup to a staging environment.
2. Run `dotnet test` against the restored DB.
3. Verify `encrypted_credentials` decrypt without errors.
4. Document the drill result in the ops runbook.

## GDPR — User Data Deletion

To delete all data for a user (right-to-erasure request):

```sql
-- Soft-delete all accounts (cascades to transactions via EF OnDelete)
UPDATE bank_accounts SET is_active = false, deleted_at = now()
WHERE user_id = '<user-uuid>';

-- Remove encrypted credentials
DELETE FROM encrypted_credentials
WHERE account_id IN (
    SELECT id FROM bank_accounts WHERE user_id = '<user-uuid>'
);
```

Confirm no plaintext PII remains in logs before closing the ticket.
