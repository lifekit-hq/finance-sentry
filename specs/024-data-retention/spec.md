# Feature Specification: Data Retention & Backups

**Feature Branch**: `024-data-retention`
**Created**: 2026-07-09
**Status**: Implemented
**Input**: User description: "Data retention and rotation: bounded growth for logs, Hangfire storage, and domain tables (audit events, daily bars, snapshots, scores) via per-module purge/downsample policies, plus automated off-host database backups with restore verification"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The database stops growing without bound (Priority: P1)

As the operator, I know that every table with unbounded growth has an explicit retention policy (purge, downsample, or keep-forever-by-decision), enforced automatically on a schedule, so the production database's size stays within a predictable budget on the single host.

**Why this priority**: Unbounded growth is a slow-motion outage: one day the disk fills and everything stops. This is the core problem the feature exists to solve.

**Independent Test**: Insert records older than a policy's cutoff into one governed table; run the retention job; verify only the out-of-policy records were removed and the run was logged.

**Acceptance Scenarios**:

1. **Given** a table governed by a purge policy of N days, **When** the scheduled retention run executes, **Then** rows older than N days are deleted and rows within N days are untouched.
2. **Given** a retention run completes, **When** the operator reviews its record, **Then** it shows per-table rows examined/removed and duration.
3. **Given** a retention run fails mid-way, **When** it re-runs, **Then** it completes correctly without double-deleting or skipping (idempotent).
4. **Given** all policies are active for 30 days of normal use, **When** total database size is measured, **Then** growth comes only from tables explicitly designated keep-forever.

---

### User Story 2 - Recoverable off-host backups (Priority: P1)

As the operator, the production database is automatically backed up on a schedule to storage *outside* the production host, and I have evidence the backups actually restore — so a disk failure or bad migration cannot destroy my financial history.

**Why this priority**: Retention deletes data on purpose; backups protect against deleting it by accident. Shipping purge automation without proven backups would be reckless — they land together.

**Independent Test**: Trigger a backup, restore it into a scratch database, verify row counts and a sample of recent records match the source.

**Acceptance Scenarios**:

1. **Given** the nightly backup schedule, **When** a backup completes, **Then** the artifact exists off-host, is encrypted, and its age is visible to the operator.
2. **Given** an existing backup, **When** the restore verification runs, **Then** it restores into an isolated target and reports success/failure without touching production data.
3. **Given** backups have run for 30 days, **When** the operator checks the backup store, **Then** old backups beyond the retention window have been pruned (backup storage is itself bounded).
4. **Given** a backup fails, **When** the operator checks system health, **Then** the failure is visible (surfaced through the observability stack).

---

### User Story 3 - Downsampling instead of deleting for history tables (Priority: P2)

As the user of the app, my long-term history (net worth over years, market bars) stays useful: old fine-grained points are compacted into coarser ones (e.g. daily → weekly) rather than disappearing, so charts still tell the full story at appropriate resolution.

**Why this priority**: Preserves product value while still bounding growth; more complex than plain purge, and only some tables need it.

**Independent Test**: Seed 2 years of daily records; run downsampling; verify recent data stays daily, older data is aggregated at the coarser resolution, and charts spanning the boundary still render sensibly.

**Acceptance Scenarios**:

1. **Given** history older than the fine-grained window, **When** downsampling runs, **Then** the old rows are replaced by correct aggregates and total row count drops accordingly.
2. **Given** a downsampled range, **When** the user views a multi-year chart, **Then** values across the boundary are continuous (no gaps, no double-counting).

### Edge Cases

- Retention job runs while a sync job is writing to the same table: deletion must not deadlock or remove rows the sync just wrote (cutoffs are far from "now").
- Very large first purge (years of backlog): deletion must run in bounded batches so it doesn't lock tables or spike the database for hours.
- Clock skew / timezone: cutoffs computed in UTC consistently.
- A table added by a future feature without a policy: there must be a place where "every growing table needs a retention decision" is enforced or at least surfaced (e.g. a documented policy registry the constitution points to).
- Restore verification must never be able to write to the production database.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Every persistent table MUST have an explicit, documented retention decision: purge after N days, downsample after N days, or keep forever. The decisions live in a single policy registry in the repository.
- **FR-002**: Purge policies MUST be enforced by scheduled per-module jobs that delete out-of-policy rows in bounded batches, idempotently, and record what they did.
- **FR-003**: Downsample policies MUST replace fine-grained history with correct coarser aggregates beyond a configured window, preserving chart continuity.
- **FR-004**: Application log files and background-job execution history MUST be capped by size/age so they cannot exhaust host disk.
- **FR-005**: The production database MUST be backed up automatically on a schedule, encrypted, to a location off the production host, with a bounded number of retained backups.
- **FR-006**: Backup integrity MUST be verified by an automated restore into an isolated target on a schedule (at minimum weekly), with the result visible to the operator.
- **FR-007**: Retention and backup runs MUST emit their outcomes to the observability stack (metrics/logs) so failures are noticed.
- **FR-008**: User-initiated financial records (transactions, accounts, theses, budgets) are keep-forever by default; only derived/observational data (audit events, bars, scores, snapshots, job logs) is subject to purge/downsample.

### Key Entities

- **Retention policy**: table name, action (purge/downsample/keep), window, batch size — versioned in the repo.
- **Retention run record**: timestamp, policy applied, rows removed/aggregated, duration, outcome.
- **Backup artifact**: encrypted database snapshot with creation time, size, and verification status.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After policies are active, month-over-month database growth is attributable only to keep-forever tables (verifiable from size metrics).
- **SC-002**: The operator can answer "when is the last backup that provably restores?" in under 1 minute.
- **SC-003**: A full restore drill from the latest backup completes successfully, with recent transactions present in the restored copy.
- **SC-004**: Retention runs complete inside their scheduled window without measurable impact on API latency (p95 regression < 5% during runs).
- **SC-005**: No governed table contains rows older than its policy window plus one scheduling interval.

## Assumptions

- Depends on feature 023 (observability) for surfacing run outcomes; retention can ship first but its failures would be invisible until 023 lands.
- Off-host backup target is object storage or a second host reachable from the VPS; exact target chosen at plan time.
- Initial policy windows are proposals to be tuned: audit events ~90 days, job history ~30 days, market bars downsample after ~1 year, candidate scores ~180 days; transactions/accounts/theses keep forever.
- Single-database, single-host deployment as it exists today.

## Notes

- [DECISION] Backups and retention ship as one feature: automated deletion without proven restore capability is unacceptable for financial data.
- [DECISION] Policy registry lives in the repository (reviewable, versioned), not in database configuration.
- [OUT OF SCOPE] Point-in-time recovery / WAL archiving — nightly snapshots are sufficient for a single-user system; revisit if RPO needs shrink.
- [OUT OF SCOPE] User-facing data export (GDPR-style) — separate concern, later built separately; see docs/DATA_EXPORT_GUIDE.md.
