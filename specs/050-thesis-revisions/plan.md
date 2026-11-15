# Implementation Plan: Thesis revisions

Module: Research (`Domain/Revisions`, `Application/Commands/{Propose,Decide,Expire}ThesisRevision*`, `ThesisRevisionMerge`,
`Application/Queries/GetThesisRevisionsQuery`, `Infrastructure/Persistence/Repositories/ThesisRevisionRepository`,
`Infrastructure/Jobs/ThesisRevisionExpiryJob`, `API/Controllers/ThesisRevisionsController`) and MCP
(`ProposeThesisRevisionTool`, `ListThesisRevisionsTool`, `DecideThesisRevisionTool`).

## Decisions

- **Table is the journal.** `thesis_revisions` rows are never deleted and carry status, decision note, superseding id and
  the resulting thesis id, so proposals are not mirrored into `thesis_events` (which price-stamps and feeds the track
  record). The confirm's own write is journaled by the existing commands.
- **Merge, not snapshot.** A revision stores only the fields it changes; confirm lays them over the thesis as it stands then.
- **Promotion reuses `PromoteCandidateCommand`** (new optional `Content` overrides the scorecard-built defaults), so the risk
  gate, candidate bookkeeping and the `Promoted` journal row are the single existing path.
- **Authorization** is layered: `RequireMcpPersonalToken` on the MCP tool, `RequireAiUse` on the REST route, and an actor
  check inside the handler (`IAuditActor.Kind`).
- **Re-gating** `save_thesis`/`promote_candidate`/`revert_thesis_revision` narrows #1383; `reject_candidate` and
  `watchlist` stay open to Ledger.

## Migration

`M030_ThesisRevisions`: `CREATE TABLE research.thesis_revisions` + `idx_thesis_revisions_user_status_created` + partial
unique `ux_thesis_revisions_user_subject_pending` (`Status = 'Pending'`). Down drops the table. Ordered after M029.
