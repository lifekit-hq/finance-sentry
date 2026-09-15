# No Postgres and no Docker in the sandbox — EF mapping changes cannot be proven locally

There is no `docker`, no `psql` and no local PostgreSQL server. Backend CI supplies a
`postgres:14-alpine` service container, so the DB-backed tests only really run there;
locally they self-skip (`[DockerRequiredFact]`) or fail to connect. A green
`.devclaw/verify` therefore does **not** prove a migration, an EF mapping or a
constraint change.

The workaround already in the repo is SQLite: `ThesisSqliteFixture`
(`backend/tests/FinanceSentry.Modules.Research.Tests/Persistence/`) builds a
`ResearchDbContext` over an in-memory SQLite connection with every Postgres-only
entity ignored. Copy its shape rather than inventing another. Its two hard limits,
both hit in practice:

- SQLite cannot `ORDER BY` a `DateTimeOffset`, so any repository method that orders
  by a timestamp (`ThesisRepository.ListAsync`, `ThesisEventRepository.GetLatestForSubjectAsync`)
  is untestable there — use a read path that does not order, or a test double.
- `jsonb`, `real[]` and `gen_random_uuid()` defaults must be cleared from the model.

When a change's real risk sits in the Postgres mapping, say so in the PR's Validation
section instead of implying the local run covered it.
