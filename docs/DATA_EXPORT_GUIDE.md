# Data Export

**Status: planned, not implemented.** There is no export endpoint or UI today, so no export file format
exists to document. The earlier version of this page described a JSON export that was never built.

What exists instead:

- **Your own copy of everything**: the encrypted nightly database backups described in
  [`DATABASE_BACKUP.md`](DATABASE_BACKUP.md). A self-hoster owns the database and can query it directly.
- **Erasure**: account closure and the purge job (`OPERATIONS_RUNBOOK.md` §12).

A per-user export (accounts, transactions, holdings; credentials never exported) is tracked as a separate piece of
work. This page will be replaced with the real format when it ships.
