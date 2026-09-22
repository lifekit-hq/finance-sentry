# Phase 0 Research: IPS ↔ Risk Rules Boundary Cleanup

All unknowns from Technical Context resolved below. No `NEEDS CLARIFICATION` remain.

---

## R1 — Which module owns which concept, and which readers move

**Decision**: Allocation (targets + band + rebalancing rule) → **IPS** (Research). Position cap → **Risk Rule Set**. Per spec [DECISION]: intent lives in IPS, enforced limits live in Risk.

**Reader disposition (verified against code):**

| Reader | File | Reads today | After cleanup | Action |
|---|---|---|---|---|
| Allocation drift (Research) | `GetAllocationDriftQuery` | IPS allocation + `RebalancingRule` | IPS (unchanged) | **Verify only** — already the single home |
| Allocation drift (Risk) | `RiskEvaluationService.ComputeRawViolations` L218–240 | `RiskRuleSet.AllocationTargets` | IPS via port | **Repoint** to `IAllocationPolicySource` |
| Position-cap enforcement (Risk) | `RiskEvaluationService` L158–175, L74–88 | `RiskRuleSet.MaxPositionWeightPct` | Risk (unchanged) | **Verify only** — already the single home |
| Opportunity scoring (Research) | `ScoreCandidateCommand.BuildIpsFit` L165–167 | `ips.MaxSinglePositionPct` | Risk via port | **Repoint** to `IPositionCapSource` |

**Rationale**: Only two of the four readers actually move; the other two already read what will become the single home. This shrinks the behavioural-risk surface to (a) the Risk drift check reading IPS, and (b) scoring reading the Risk cap.

**Alternatives considered**:
- *Collapse both allocation-drift evaluators into one shared computation.* Rejected — the two evaluators compute bands differently (Research: `Max(abs, target·rel/100)`; Risk: per-sleeve `DriftBandPct`) and feed different consumers (MCP drift DTO vs compliance `PolicyViolation`). Merging them would change verdicts → violates SC-002. Keep both computations; only change the Risk one's *data source*.
- *Drop the Risk `AllocationDrift` violation entirely.* Rejected — removes verdicts a consumer (compliance report / `RiskCheckJob`) relies on; a behaviour regression.

---

## R2 — Cross-module read mechanism (Principle I)

**Decision**: Two read-only ports, each owned by the consuming module's Domain, implemented in that module's Infrastructure by delegating to the *other* module's existing query handler.

- `Risk.Domain.Ports.IAllocationPolicySource` → `Risk.Infrastructure.Adapters.IpsAllocationPolicySource` → calls Research `IQueryHandler<GetIpsQuery, IpsDto?>`.
- `Research.Domain.Ports.IPositionCapSource` → `Research.Infrastructure.Adapters.RiskPositionCapSource` → calls Risk `IQueryHandler<GetRiskRuleSetQuery, RiskRuleSetDto?>`.

**Rationale**: Constitution Principle I forbids a module coupling to another's concrete internals/DbContext; it mandates domain-defined interfaces resolved via DI. Delegating to the existing public query handlers (not the repository or DbContext) keeps the dependency at the sanctioned contract layer and reuses tested read logic. The port returns a small, module-local shape (not the foreign DTO) so neither domain leaks into the other.

**On the apparent Research↔Risk bidirectional read**: Risk reads Research (allocation) and Research reads Risk (cap). This is not a cycle at the domain level — each side depends only on an *interface* it owns; the concrete adapters live in Infrastructure and are wired at the composition root. No compile-time module cycle (adapters reference the other module's Application query contracts, which is already how MCP tools reference both). Confirmed acceptable.

**Alternatives considered**: direct `DbContext`/repository injection across modules (rejected — Principle I automatic-block); a shared "policy" module owning both concepts (rejected — new module, violates FR-015 and over-scopes a cleanup).

---

## R3 — Position-cap reconciliation & unit normalization

**Problem**: IPS `MaxSinglePositionPct` (`numeric(6,2)`, **no save-time unit validation** — `SaveIpsCommand` passes it straight through) vs Risk `MaxPositionWeightPct` (`numeric(9,6)`, validated **fraction (0,1]**). Scoring compares a fraction `currentWeight` to the IPS value, so an IPS value stored as whole percent (e.g. `25`) silently never bites; stored as `0.25` it works.

**Decision — reconciliation rule (position cap), applied in the Research M012 migration** (the migration that drops the IPS cap, so it consumes that value before dropping it and writes the survivor into the retained Risk cap column):
1. Read both `investment_policy_statements.MaxSinglePositionPct` (current row, per user) and `risk_rule_sets.MaxPositionWeightPct` (current row, per user).
2. **Normalize** the IPS value to Risk's fraction unit: if `value > 1` treat as whole percent → `value / 100`; else treat as already a fraction. Log the normalization.
3. **Survivor = stricter (lower) cap** among the (normalized) values that are present (FR-009). If only one present, it survives (FR-010). If neither present, leave Risk cap `NULL` — fabricate nothing (FR-010).
4. Record any discarded value + the rule that chose the survivor to the migration/audit log (FR-011).
5. Write the survivor into `risk_rule_sets.MaxPositionWeightPct` **only if it differs** from the current value (idempotency — FR-012).

**Rationale**: Never loosen a safety limit during migration (stricter wins). Normalization prevents importing an ambiguous IPS whole-percent value as a nonsensical >1 "fraction" that would fail Risk validation and mis-enforce.

**Validation dependency (see R7)**: the real user's live values must be checked on the VPS before finalizing — if the IPS cap is `NULL` in production (expected, since Risk owns the enforced/REST/validated cap), the normalization branch never fires and reconciliation is a trivial no-op. The rule is still implemented for correctness/future users.

---

## R4 — Allocation reconciliation & shape translation

**Problem**: Allocation's single home is IPS, but the *Risk* drift evaluator consumes `AllocationTargetEntry(AssetClass, TargetPct[fraction], DriftBandPct[fraction])` and compares against fractional book weights. IPS stores `AllocationTarget(AssetClass, TargetPct[whole %], MinPct, MaxPct)` + a global `RebalancingRule`. To keep the Risk drift verdict byte-for-byte, the repointed reader must recover the exact per-sleeve `TargetPct` and `DriftBandPct` the Risk evaluator used.

**Decision — reconciliation rule (allocation), applied in the Risk M002 migration** (the migration that drops the Risk allocation column) **+ the repointed reader**:
- **Reconciliation (migration)**: allocation survivor = **IPS value** when both present (FR-009: intent record wins). When only Risk had allocation (IPS empty), copy Risk → IPS *reversibly*: for each entry, `IPS.TargetPct = Risk.TargetPct · 100`, `IPS.MinPct = (Risk.TargetPct − Risk.DriftBandPct) · 100`, `IPS.MaxPct = (Risk.TargetPct + Risk.DriftBandPct) · 100`. When both empty, fabricate nothing. Log discarded Risk allocation when IPS wins.
- **Repointed Risk reader (`IAllocationPolicySource` → translate IPS → drift tuple)**: for each IPS `AllocationTarget`, produce `TargetPct(fraction) = IPS.TargetPct / 100` and `DriftBandPct(fraction)` recovered as `((MaxPct − MinPct) / 2) / 100` when `MinPct/MaxPct` are set (`> 0`), else derive from `RebalancingRule` exactly as `GetAllocationDriftQuery` does: `band = Max(AbsoluteBandPct, TargetPct · RelativeBandPct / 100) / 100`. This mirrors the existing Research derivation so both drift evaluators stay consistent and the Risk verdict reproduces its prior value.

**Rationale**: The symmetric `Min/Max = target ± band` encoding is exactly reversible, so a Risk-origin band round-trips to the same `DriftBandPct`. Where allocation was IPS-native (the intended future state), the reader derives the band from the same rule the Research evaluator already uses — no divergence.

**Precision note**: pin decimal rounding to match existing `numeric` scales (`MaxSinglePositionPct numeric(6,2)`; Risk caps `numeric(9,6)`). Characterization tests (R6) catch any rounding drift.

**Alternatives considered**: storing a dedicated symmetric `DriftBandPct` on IPS (rejected — adds a field to the intent record for a limits-side concept, muddies the very boundary we're drawing; the Min/Max encoding already carries the band losslessly).

---

## R5 — Migration ordering, cross-schema, idempotency

**Decision — each migration reconciles the concept whose column IT drops (order-independent)**:
- Two EF migrations: Risk **M002** (drops `risk_rule_sets.allocation_targets_json`) and Research **M012** (drops `investment_policy_statements.MaxSinglePositionPct`).
- **Each migration reconciles *before its own drop*, writing the survivor into the *other* schema's *retained* column.** Because `research` and `risk` share one physical Postgres DB, cross-schema read/write is available within a single migration:
  - **Risk M002** = the **allocation** concept: read `risk...allocation_targets_json`, reconcile into `research...AllocationTargets` (IPS-wins; else copy Risk→IPS reversibly), log discards, then drop the Risk allocation column.
  - **Research M012** = the **position-cap** concept: read `research...MaxSinglePositionPct`, normalize unit, stricter-wins vs `risk...MaxPositionWeightPct`, write to the retained Risk cap column, log discards, then drop the IPS cap column.
- **No cross-context apply-order dependency.** Each migration reads only the column it is about to drop and writes only to a column no migration drops. Whichever context applies first moves its concept out safely; the other's target column is always present. This removes the data-loss risk of M012 dropping the IPS cap before a Risk-side migration could read it. Validated by applying in **both** orders in tests (T017).
  - **Superseded (#661)**: on a brand-new database M012 still needs `risk.risk_rule_sets` to *exist* (created by Risk M001), so there is a schema-creation order dependency after all — `MigrationExtensions.MigrateAllModules` runs Research up to M011, then Risk, then the rest of Research.
- **Idempotency (FR-012, SC-004)**: every write is guarded (`WHERE target IS DISTINCT FROM survivor`); re-running after consolidation performs zero writes.
- **Rollback**: `Down()` re-adds the dropped columns (nullable, empty) — data is not restored (documented one-way data consolidation); acceptable because the surviving single home retains the reconciled value.

**Rationale**: single-DB cross-schema SQL is the least-moving-parts way to reconcile without a bespoke data-migration job. Assigning each concept's reconciliation to the migration that drops that concept's column makes the two migrations independent — no fragile runtime ordering to enforce across EF contexts.

**Alternatives considered**: a one-shot Hangfire backfill job (rejected — more moving parts, harder to make transactional with the schema change); app-startup reconciliation (rejected — non-idempotent risk, ordering coupling).

---

## R6 — Proving zero behavioural drift (SC-002)

**Decision**: characterization ("golden-master") tests, captured **before** the change and asserted **after**:
1. **Risk drift + cap enforcement**: seed `RiskEvaluationService` with representative `BookSnapshot`s and a rule set where allocation & cap agree with the IPS copy; snapshot the emitted `PolicyViolation` list (keys, actual/limit/excess/severity); assert identical after repoint.
2. **Opportunity scoring**: seed `ScoreCandidateCommand` with holdings + a cap value present in both records (same normalized fraction); snapshot `IpsFitFacts` (`withinConcentration`, cap surfaced) and the final score; assert identical after repoint.
3. **Research drift**: `GetAllocationDriftQuery` output snapshot (no source change, guards against accidental regression).
4. **Reconciliation matrix (US3)**: unit tests for (a) matching, (b) differing cap → stricter wins, (c) differing allocation → IPS wins, (d) one-side-empty → populated survives, (e) both-empty → unset, (f) unit-ambiguous IPS cap normalized correctly, (g) re-run → no change (idempotent).
5. **Live-data check**: capture the real user's current drift verdicts / cap outcome / candidate scores on the VPS pre-migration, and re-capture post-migration; record any intended change (R7).

**Rationale**: byte-for-byte equality on real evaluator outputs is the only credible evidence the cleanup didn't move behaviour; the matrix proves the migration rule; the live check catches the one honest place behaviour *may* change (disagreeing copies) and documents it rather than hides it.

---

## R7 — Live production data (informs R3/R4 finalization)

**Decision**: before implementing the migration, query the VPS Postgres for the single active user's current `investment_policy_statements.MaxSinglePositionPct`, `investment_policy_statements.AllocationTargets`, `risk_rule_sets.MaxPositionWeightPct`, and `risk_rule_sets.allocation_targets_json`. This determines which reconciliation branches actually fire and whether repointing scoring to the Risk cap changes any live score.

**Expected (per spec assumption + code)**: the enforced/validated/REST-exposed cap lives in Risk; the IPS cap is likely `NULL` → cap reconciliation is a no-op and scoring gains a real (correct) cap where it previously had none. Allocation likely lives in exactly one record. Confirm, don't assume; record the finding in the PR/change log (memory: production data lives on VPS, not local).

**Rationale**: the reconciliation rule must be *correct* for all cases, but knowing the *actual* case lets us state precisely in the changelog what did/didn't change for the real user — which is the trust-preserving deliverable of a "zero-drift" cleanup.

---

## R8 — Contracts & versioning

**Decision**:
- Drop `MaxSinglePositionPct` from: MCP `save_ips` param, `SaveIpsCommand`, `GetIpsQuery` projection, `IpsDto`. (MCP-only; no REST surface.)
- Drop `AllocationTargets` from: MCP `save_risk_rules` param, `SaveRiskRuleSetCommand` (+ its per-target validation), `GetRiskRuleSetQuery`/`RiskRuleSetDto`, REST `SaveRiskRulesRequest`.
- **Backend API version bump + git tag** in the PR (Risk REST `PUT`/`GET /risk/rules` request/response schema changes) — constitution Versioning & Tagging. Field removal is breaking-shaped; no live SPA consumer (grep-verified) softens real-world impact — classify per policy and note the absence of clients in release notes.
- **Contract test** for `PUT`/`GET /risk/rules` asserting the response/request schema no longer carries `AllocationTargets` and that posting it is ignored/rejected (SC-005).
- **FR-014 flag**: the moved MCP fields (`save_ips.maxSinglePositionPct`, `save_risk_rules.allocationTargets`) and their new homes are called out in the change record for the agent-config (Ledger persona) owner — the agent-side prompt update is out of scope (performed on OpenClaw).

**Rationale**: contracts must stop advertising the moved field in the wrong home or the agent keeps writing to a black hole (FR-013), reintroducing the ambiguity we removed.
