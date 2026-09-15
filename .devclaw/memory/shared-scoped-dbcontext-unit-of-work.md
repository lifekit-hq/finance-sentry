# Every repository in a module shares one scoped DbContext — a bare SaveChangesAsync is not local

`ResearchDbContext` (and its siblings) are registered `AddDbContext` = **scoped**, and each
repository takes it by constructor injection. So `ThesisRepository`, `ThesisEventRepository` and
`QuoteCacheRepository` all hold *the same instance* inside one request. A repository method that
calls `db.SaveChangesAsync()` therefore commits **everything** pending in that request, including
work another component staged — and, on failure, leaves that work staged for the next caller to
trip over.

That is the mechanism behind issue #626. `SaveThesisCommandHandler` committed the thesis, then its
Created-event hook refreshed quotes; `QuoteCacheRepository.UpsertManyAsync` failed, its rows stayed
`Added` in the tracker, `ThesisEventRecorder.TryGetPricesAsync` swallowed the exception as
best-effort, and the very next `SaveChangesAsync` (the event append) re-attempted the poisoned
insert and threw. Reads were unaffected; `run_thesis_monitor` was unaffected because it already
guarded the recorder. Every `save_thesis` failed, regardless of payload.

Two rules this leaves behind:

- A **best-effort** write whose failure a caller swallows must roll its own staged entries back out
  of the change tracker before the exception escapes. `QuoteCacheRepository.UpsertManyAsync` shows
  the shape (detach `Added`, restore `Modified` from `OriginalValues`).
- A **post-commit side-effect** (journal, audit, cache) must be guarded at its call site, as
  `RunThesisMonitorCommandHandler.TryRecordEventAsync` and
  `SaveThesisCommandHandler.TryRecordCreatedAsync` both do. Letting it throw fails a call whose
  primary write already committed — and a retried create mints a fresh id, so the retry duplicates
  the row rather than replacing it.
