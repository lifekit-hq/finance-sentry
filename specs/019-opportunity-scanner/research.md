# Phase 0 Research: Opportunity Scanner

Grounded in the implemented 017/018/020/022 code (2026-07-08) + spec decisions.

## R1 — Home: Research module
- **Decision**: Candidate + score live in Research (`ResearchDbContext`, migration M006).
- **Rationale**: Research already owns every in-process dependency — thesis/SaveThesis (017), EDGAR
  fundamentals, watchlist, IPS (M003), thesis-events (020). Only structure (018) and risk-gate (022)
  are external, handled by Core seams (R2/R3).

## R2 — Read 018 structure via new Core `IMarketStructureReader`
- **Decision**: Add `IMarketStructureReader.GetStructureAsync(ticker, ct) → MarketStructureSnapshot`
  (Core DTO) implemented in Radar over the existing `IStructureQueryService.GetStructureAsync` →
  `TickerStructure` (RsByWindow, ExtensionFromMa50, TodayZScore, VolumeRatio, Stale, etc.).
- **Rationale**: `IStructureQueryService` and `TickerStructure` are Radar-internal domain types; a
  Research→Radar reference would violate Principle I. Mirrors the `IRadarSignalWriter`/`IWatchlistReader`
  Core-seam pattern. The Core DTO carries exactly the fields the structure sub-score + crowding need.
- **Alternatives**: Inject Radar's query service directly — rejected (module coupling).

## R3 — Call 022 gate via new Core `IRiskPolicyGate`
- **Decision**: Add `IRiskPolicyGate.CheckProposalAsync(userId, ticker, proposedUsd, overrideFlag, ct)
  → RiskGateVerdict` (Core DTO: Decision `Allowed|Refused`, RuleKey?, ObservedValue?, LimitValue?,
  MaxCompliantSizeUsd?, Note?) implemented in Risk, adapting `CheckRiskRulesQueryHandler`
  (`CheckRiskRulesQuery(userId, RiskProposal(ticker, proposedUsd, override))` → `CheckRiskRulesResult.Verdict`).
- **Rationale**: The 022 gate is a Risk-internal `IQuery` returning Risk-domain `RiskVerdict`. Research
  must not reference Risk. A Core interface wraps it (FR-011b). When the verdict is `Refused`, promotion
  refuses with the named rule unless `overrideFlag` is set; an override is recorded as a signal (FR-007,
  reuses 022's `risk_override` path or emits an `opportunity` override signal).
- **Alternatives**: Inject the query handler cross-module — rejected.

## R4 — Candidate lifecycle events via existing `IThesisEventRecorder`
- **Decision**: On promote/reject/expire, call `IThesisEventRecorder.RecordAsync(userId,
  ThesisSubjectType.Candidate, candidateId, ticker, ThesisEventType.{Promoted|Rejected|Expired},
  decisionNote?, ct)`. The recorder + enums already model `Candidate` and these event types (built inert
  in 020) — this feature activates them. On `Created` (first score) record a Created candidate event too.
- **Rationale**: FR (020 SC-004 counterfactuals). In-module (Research) — no new seam. Non-blocking
  (recorder never throws on quote failure).

## R5 — Scorecard: sub-scores + evidence, NO composite (FR-007)
- **Decision**: Three deterministic sub-scores/sections computed by pure functions, versioned by
  `FormulaVersion`:
  - **Structure (0–100)**: fixed piecewise normalization of RS(21/63d), sector rank/rank-delta,
    breakout state (price vs 63-day high). Inputs from `MarketStructureSnapshot`.
  - **Fundamentals (0–100)**: revenue YoY, gross/operating margin level + 4-quarter trend, EPS YoY,
    normalized by fixed rules; **not-evaluable** when EDGAR data missing (never neutral-faked).
  - **Crowding class** (`Early|Normal|Extended`): from extension-from-MA50 + volume ratio by config thresholds.
  - **IPS-fit** (facts, not a score): current + would-be position weight vs `IPS.MaxSinglePositionPct`,
    asset-class fit, existing sector exposure.
- **Rationale**: FR-002/003/004/005/006/007. Each sub-score stores its raw inputs/periods/windows in
  `Evidence` (jsonb) — 100% explainable (SC-001). No weighted blend (unbacktested weights = false precision).
- **Alternatives**: A single composite score — explicitly rejected by the 2026-07-07 review.

## R6 — Reuse 017 fundamentals math (no silent duplication)
- **Decision**: Extract shared concept-name mapping + margin/YoY helpers from
  `ThesisBreakEvaluator` into `Domain/Scoring/FundamentalMath.cs`; both the evaluator (017) and the
  fundamentals scorer (019) use it. If extraction risks touching 017 behaviour, the minimum bar is to
  reuse the `ThesisMetric` concept constants and replicate ratio formulas with a shared source — never
  a second silent copy of the concept table.
- **Rationale**: The grounding flagged `ThesisBreakEvaluator` returns verdicts, not raw ratios; 019
  needs raw ratios for scoring. Extract, don't fork.

## R7 — Promotion prefills triggers deterministically (FR-011)
- **Decision**: `TriggerPrefill` produces proposed `ThesisInvalidationTrigger`s from the scorecard,
  caller-overridable: (a) no `price_drawdown` trigger (`TriggerPrefill` proposes fundamentals triggers only; a caller-supplied one counts the period type it names, quarterly by default); (b) if
  revenue YoY currently positive: `revenue_yoy lessThan 0` for 2 quarters; (c) if gross margin
  evaluable: `gross_margin lessThan <latest gm − config buffer 0.10>` for 2 quarters. Rounded, cited.
  Promotion calls `SaveThesisCommand` (validates via `ThesisTriggerVocabulary`) and links the
  candidate (`PromotedThesisId`, status `Promoted`). All three metrics are in the 017 vocabulary.
- **Rationale**: FR-011; SC-004 (promoted thesis immediately valid for the 017 monitor).

## R8 — Promotion runs the risk gate (FR-011b)
- **Decision**: `PromoteCandidateCommand` calls `IRiskPolicyGate.CheckProposalAsync` before creating
  the thesis; `Refused` aborts with the named rule + max compliant size unless `overrideFlag`; an
  override records an opportunity/risk override signal (never silent — FR-007).
- **Rationale**: FR-011b; the 022↔019 contract (022 SC-003).

## R9 — Expiry job + retention
- **Decision**: `CandidateExpiryJob` (daily) expires `Active` candidates past `ExpiresAt` (TTL default
  30d, config), recording a final `CandidateScore` snapshot + an `Expired` event. Nothing is deleted —
  rejects/expired stay queryable (SC-005, feeds 020 counterfactuals).

## R10 — Signals + alerts
- **Decision**: On score, emit an `info` nomination signal via `IRadarSignalWriter`
  (Scanner `opportunity`); a top-tier candidate emits a `notable` signal + at most one Alert per
  candidate per silence window (new `AlertType.Opportunity`, `GenerateOpportunityAlertAsync`).
- **Rationale**: FR-010. "Top tier" without a composite = a sub-score threshold rule (e.g. structure ≥
  configured bar AND fundamentals evaluable ≥ bar), config-driven.

## R11 — Deferrals
- **US2 `scan_opportunities`** (machine scan) → v2, gated on 018 calibration (spec). 5th tool ships then.
- **FR-006b/c** (valuation "what's priced in" + base-rate annotation) → v1.1; both need a seeded
  reference dataset. Deferring avoids inventing authoritative base-rate numbers. `Evidence` leaves a slot.
