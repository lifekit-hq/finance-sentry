# Data Export

A signed-in person downloads their own data from **Settings → Your data → Download my data**, which calls
`GET /api/v1/account/export` (any signed-in person, any role). The response is one JSON file,
`finance-sentry-export-YYYY-MM-DD.json`, streamed as it is written:

```json
{
  "format": "finance-sentry-export/1",
  "exportedAt": "2026-11-03T12:00:00Z",
  "userId": "<uuid>",
  "modules": {
    "Auth": { "users": [ ... ], "roles": [ ... ], "permissionGrants": [ ... ] },
    "BankSync": { "bank_accounts": [ ... ], "transactions": [ ... ] },
    "Budgets": { "...": [ ... ] }
  }
}
```

Each module holds one array per table, named for the table, with the person's rows as camelCase objects of their mapped
columns (enums as names). A child row with no owner column of its own is nested in its parent (an Agent conversation
carries its `messages`). `Auth` is the exception: it is written column by column, as `users` (profile and preferences),
`roles` (role names), `permissionGrants` and `audit_events`.

**Scope.** The person is taken from the signed-in principal only. The export reuses the per-module erasers' boundaries
(`IUserDataExporter` mirrors `IUserDataEraser`; the owned-row sweep walks the same `IUserOwned` model walk and the same
`UserId` predicate, with every query filter off), so it holds exactly what closing the account deletes, including
archived and removed rows. A test pins that every eraser module has an exporter, and an integration test seeds two
people across the whole schema and asserts nothing of the other person reaches the file.

**Never exported:**

- Anything that lets someone act as the person: the sign-in password hash and security stamps, external logins,
  refresh tokens, MCP authorization codes and service tokens, push-subscription endpoints and keys.
- Stored provider credentials: every `byte[]` column (the encrypted keys, their IVs and auth tags) and the plaintext
  IBKR consumer key, access token and DH parameter. A test fails the build on a new secret-looking column that is
  neither dropped nor reviewed.
- Embedding vectors and other `float[]` columns, and computed properties EF does not map.
- Another person's data, and shared research chunks (only the person's own research documents, theses, candidates and
  scores are written).

Account-closure records are not part of the export: they are the operator's record of the closure, not the person's
data. The person's own **audit events** are (`audit_events` in the Auth module): when their declared profile data was
written, or read through MCP or the in-app agent, by whom (actor kind), which resource id and which field names - never
a value. A field-encrypted column of an owned entity is exported decrypted: it is the person's own data, and it is a
text column, not a dropped `byte[]`.

## Other ways to get or remove your data

- **A full copy of everything**: the encrypted nightly database backups described in
  [`DATABASE_BACKUP.md`](DATABASE_BACKUP.md). A self-hoster owns the database and can query it directly.
- **Erasure**: a person closes their own account in Settings (it erases every module's rows after the grace period;
  see the privacy notice at `/privacy`) and the purge job runs it (`OPERATIONS_RUNBOOK.md` §12).
