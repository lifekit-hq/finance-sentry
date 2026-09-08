# Feature Specification: News Source Dark Recovery

**Feature Branch**: `goal/fs-318-trendforce-sync-2026-08-31` (continues the branch that shipped 046)

**Created**: 2026-09-07

**Status**: US1 implemented; US2 deferred

**Origin**: issue #318 / spec [046](../046-trendforce-source-recovery/spec.md) — the residual defect that
investigation exposed but did not fix. No separate GitHub issue; #318 is closed.

## Context

Spec 046 diagnosed why the `TrendForce Press Center` source stopped ingesting and fixed the three
links of that specific chain: the row pointed at the wrong URL, the seed job could not repair a row
keyed by the URL that was wrong, and the failure counter was sticky. The self-healing seed repair
shipped in PR #552 rescues *that* row, by name, off *that* URL.

What it did not fix is the reason nobody noticed for six weeks. Established from the code, not
assumed:

1. `NewsSourceHealthTracker.RecordFailure` sets `Enabled = false` at 12 consecutive failures.
2. `NewsIngestionJob.IngestRegisteredSourcesAsync` iterates `INewsSourceRepository.ListEnabledAsync`,
   which is `Where(s => s.Enabled)` — so a retired source is never fetched again.
3. Nothing in the codebase sets `Enabled = true` on its own. The only two paths back are
   `RegisterThesisSourceCommand` (a human or Ledger re-registering the source by hand) and the
   URL-specific repair added by 046. Grep confirms `Enabled = false` is written in exactly one
   place — the auto-disable — so a disabled row always means "auto-retired", never "the user turned
   this off".

The consequence is a ratchet: **any** news source that fails for six hours is retired permanently,
whatever the cause. A site that was down for a morning, a TLS blip, a markup drift we later fixed in
code, a URL we later corrected — all end in the same dark row that never retries and, after its
one-shot disable alert, never mentions itself again. 046 hand-rescued one row from that state; the
mechanism that put it there is untouched, and the next source to hit it needs the same bespoke fix.

Deployment makes this worse, not better: fixing a parser in code (as #342 did on 2026-08-05) cannot
revive the row, because the row is not in the ingestion loop to notice that its cause of failure is
gone.

---

## User Scenarios

### [US1] A retired source comes back on its own once it can fetch again (P1)

The system periodically re-probes retired sources. A probe that succeeds returns the source to
service; a probe that fails leaves it retired and records the current reason, so the next diagnosis
starts from a fresh error rather than one from weeks ago.

**Acceptance Scenarios**:

1. **Given** a source auto-disabled with 12+ consecutive failures whose page now fetches and parses,
   **When** the recovery probe runs, **Then** the source is re-enabled with its failure counter at 0,
   `LastFailureReason` cleared and `LastSuccessAt` stamped — so the very next ingestion run includes
   it.
2. **Given** the same source, **When** the recovery probe succeeds, **Then** the articles the probe
   fetched are inserted (deduped by content hash) and tagged with the source's thesis by the same
   rules the ingestion path uses — a probe is a real fetch, not a ping.
3. **Given** a retired source whose cause of failure is still present, **When** the probe runs,
   **Then** it stays disabled, its consecutive-failure count is not inflated (that counter counts
   failures *in service*), and its `LastFailureReason` is replaced with the current error.
4. **Given** several retired sources of which one throws, **When** the probe runs, **Then** the
   remaining sources are still probed — one dead site cannot block the recovery of another.
5. **Given** no retired sources, **When** the probe runs, **Then** it performs no fetch at all.
6. **Given** a retired source, **When** the probe fails, **Then** no sync-failure alert is raised —
   the disable alert already fired once, and a probe that confirms a known-dark source must not
   re-alert every six hours.

### [US2] A source that stays dark keeps announcing itself (P2)

*Not in this slice.* The disable alert fires once; a source that remains dark past a grace window
should re-surface on a bounded cadence (or through the weekly brief) rather than relying on someone
running `list_news_sources`. Deferred: US1 removes the permanence, US2 removes the silence, and US1
is the one that recovers sources without a human at all.

---

## Functional Requirements

- **FR-047-01** A scheduled probe MUST attempt a real fetch of every auto-disabled news source.
- **FR-047-02** A probe that succeeds MUST return the source to service: `Enabled = true`,
  `ConsecutiveFailures = 0`, `LastFailureReason = null`, `LastSuccessAt` stamped.
- **FR-047-03** A successful probe's articles MUST be persisted through the same insert + thesis
  tagging rules as scheduled ingestion.
- **FR-047-04** A probe that fails MUST leave `Enabled` and `ConsecutiveFailures` untouched and MUST
  record the current failure reason.
- **FR-047-05** A failing probe MUST NOT raise a sync-failure alert and MUST NOT stop the probe run.
- **FR-047-06** The probe MUST NOT fetch anything when no source is disabled.

## Success Criteria

- **SC-001** Unit tests cover revival, article persistence, the still-failing path, isolation between
  sources, and the no-disabled-sources no-op.
- **SC-002** `dotnet test backend/FinanceSentry.sln -c Release` passes with no new failures.
- **SC-003** `dotnet build backend/FinanceSentry.sln -c Release` introduces no new warnings.
- **SC-004** *(post-deploy, outside this contract — the sandbox cannot reach the VPS)* the
  `research-news-source-recovery` recurring job is listed in the Hangfire dashboard and its runs log
  the probe outcome per disabled source.
