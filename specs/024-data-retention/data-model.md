# Phase 1 Data Model: Data Retention & Backups

**Feature**: 024-data-retention | **Date**: 2026-08-06

New persistence lives in a single new module `FinanceSentry.Modules.Retention` →
`RetentionDbContext` (schema `retention`, history table `__ef_migrations_history_retention`),
migration `M001_InitialSchema`. No changes to any existing module's schema.

---

## Registry types (compiled, not persisted)

### `RetentionPolicy` (record)

The registry entry. Lives in code (`RetentionPolicyRegistry.All`), never in the DB.

| Field | Type | Notes |
|---|---|---|
| `Schema` | string | Postgres schema (e.g. `bank_sync`) |
| `Table` | string | Physical table name (e.g. `companion_events`) |
| `TimestampColumn` | string? | Column the cutoff compares against; null for `Keep` |
| `Action` | `RetentionAction` enum | `Purge` \| `Downsample` \| `Keep` |
| `WindowDays` | int? | Rows older than this are out-of-policy; null for `Keep` |
| `BatchSize` | int | Delete batch size (default 5_000) |
| `EnforcedBy` | `RetentionEnforcer` enum | `Generic` \| `Bespoke` (with `BespokeJobName`) |
| `BespokeJobName` | string? | e.g. `AlertPurgeJob` when `EnforcedBy=Bespoke` |
| `Notes` | string? | Rationale for reviewers |

**Validation / invariants** (unit-tested):
- `Action=Purge`/`Downsample` ⇒ `TimestampColumn` and `WindowDays` non-null.
- `Action=Keep` ⇒ `WindowDays`/`TimestampColumn` null.
- `EnforcedBy=Bespoke` ⇒ `BespokeJobName` non-null; the generic engine skips these.
- No duplicate `(Schema, Table)`.
- **Coverage guard**: every table mapped by every registered `DbContext` appears exactly once in `RetentionPolicyRegistry.All` (reflection test).

---

## Persisted entities

### `RetentionRun` → `retention.retention_runs`

One row per generic purge or downsample job execution.

| Column | Type | Constraints |
|---|---|---|
| `Id` | uuid | PK |
| `RunType` | text | `Purge` \| `Downsample` |
| `StartedAt` | timestamptz | not null |
| `CompletedAt` | timestamptz | nullable until done |
| `Outcome` | text | `Success` \| `PartialSuccess` \| `Failed` |
| `TableResults` | jsonb | `[{ "table": "bank_sync.SyncJobs", "examined": N, "removed": M }]` |
| `Error` | text | nullable; failure detail (no secrets) |

Index: `(RunType, StartedAt desc)`. Duration is derived (`CompletedAt - StartedAt`).

### `BackupRun` → `retention.backup_runs`

One row per backup creation and per restore-verification.

| Column | Type | Constraints |
|---|---|---|
| `Id` | uuid | PK |
| `Kind` | text | `Backup` \| `RestoreVerify` |
| `CreatedAt` | timestamptz | not null |
| `ArtifactKey` | text | R2 object key (e.g. `daily/2026-08-06T02-00-00Z.dump.age`); null for a pure verify run that references another row |
| `SizeBytes` | bigint | nullable |
| `Sha256` | text | integrity hash of the encrypted artifact |
| `Encrypted` | bool | always true for real artifacts |
| `VerificationStatus` | text | `Pending` \| `Verified` \| `Failed` |
| `VerifiedAt` | timestamptz | set when a restore drill proves the artifact |
| `Error` | text | nullable |

Indexes: `(CreatedAt desc)`, partial `(VerifiedAt desc) WHERE VerificationStatus='Verified'`
(powers SC-002 "last provably-restorable backup").

**State transition (backup artifact)**: `Backup` job inserts `Pending` → `RestoreVerify`
job flips the referenced artifact to `Verified` (sets `VerifiedAt`) or `Failed`.
Pruning deletes R2 objects + their `backup_runs` rows beyond the retention window.

---

## Config binding

### `RetentionOptions` (bound from `Retention:` config)

| Key | Default | Purpose |
|---|---|---|
| `PurgeHourUtc` | 3 | when the nightly purge runs |
| `WindowOverrides` | `{}` | `schema.table → days` overrides of registry defaults |
| `DefaultBatchSize` | 5000 | batch size when a policy doesn't specify |
| `Downsample:Enabled` | false | P2 gate |

### `BackupOptions` (bound from `Backup:` config, secrets from `.env.sops`)

| Key | Source | Purpose |
|---|---|---|
| `BackupHourUtc` | config | nightly backup time (default 2) |
| `R2:Endpoint` / `R2:Bucket` / `R2:AccessKey` / `R2:SecretKey` | `.env.sops` | R2 target |
| `AgeRecipient` | `.env.sops` (`BACKUP_AGE_RECIPIENT`) | encrypt public key |
| `AgeIdentity` | `.env.sops` (`BACKUP_AGE_IDENTITY`) | decrypt private key (restore-verify only) |
| `RetainDaily` / `RetainWeekly` | config | prune windows (default 30 / 8) |
| `RestoreVerifyDay` | config | weekly drill day |

---

## Relationships

`RetentionPolicyRegistry` (compile-time) drives the generic `RetentionPurgeJob`, which
writes `RetentionRun` rows. `BackupJob` writes `BackupRun` rows; `RestoreVerifyJob`
updates them. No FKs across modules — `retention` schema is self-contained; it references
other tables only by name string from the registry, executed as raw batched SQL.
