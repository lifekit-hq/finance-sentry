# Data Export & Import Guide

## Export Format

User data is exported as a JSON file with the following structure:

```json
{
  "exportedAt": "2026-03-29T12:00:00Z",
  "userId": "<uuid>",
  "accounts": [
    {
      "accountId": "<uuid>",
      "bankName": "Chase",
      "accountType": "checking",
      "accountNumberLast4": "1234",
      "currency": "USD",
      "currentBalance": 1500.00,
      "syncStatus": "active",
      "createdAt": "2026-01-01T00:00:00Z",
      "encryptedCredentials": "<REDACTED — not exported>"
    }
  ],
  "transactions": [
    {
      "transactionId": "<uuid>",
      "accountId": "<uuid>",
      "amount": 45.00,
      "transactionDate": "2026-03-01",
      "postedDate": "2026-03-02",
      "description": "Supermarket XYZ",
      "merchantCategory": "Groceries",
      "transactionType": "debit"
    }
  ]
}
```

**Never exported:** access tokens, encryption keys, full account numbers.

## Triggering an Export (Developer)

```sql
-- Export all active transactions for a user
COPY (
    SELECT t.id, t.account_id, t.amount, t.transaction_date, t.description, t.merchant_category
    FROM transactions t
    JOIN bank_accounts ba ON ba.id = t.account_id
    WHERE ba.user_id = '<user-uuid>'
      AND t.is_active = true
) TO '/tmp/user_export.csv' CSV HEADER;
```

## Import / Restore

1. Re-link bank accounts through the provider consent flows (credentials cannot be imported — must re-authenticate).
2. Import historical transactions:
   ```sql
   INSERT INTO transactions (id, account_id, user_id, amount, transaction_date, ...)
   SELECT ... FROM import_staging WHERE NOT EXISTS (
       SELECT 1 FROM transactions t WHERE t.unique_hash = import_staging.unique_hash
   );
   ```
3. Run deduplication check after import.

## GDPR Compliance — Right to Erasure

To fully delete a user's data:

1. Soft-delete all accounts (`is_active = false`).
2. Soft-delete all transactions (cascade via EF or manual SQL).
3. Delete `encrypted_credentials` rows.
4. Log the erasure event in a separate compliance log (outside the main DB).

Retain: billing records and fraud prevention records as required by applicable law (typically 5–7 years).

## Data Retention Policy

Transactions older than **24 months** are automatically soft-archived by `DataRetentionJob` (runs monthly). Archived rows have `is_active = false` and `archived_reason = 'retention_policy_24m'`. They are not visible in the user UI but remain in the database for compliance queries.
