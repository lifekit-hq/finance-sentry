# Feature Specification: Opportunity Scanner

**Feature Branch**: `019-opportunity-scanner`
**Created**: 2026-07-07
**Status**: Implemented
**Input**: The "offense" of the Radar (see `specs/ROADMAP.md`): deterministic scoring of opportunity candidates from two entry paths — Denys's own convictions and machine scans of market structure — with a promote flow that turns a strong candidate into a monitored `InvestmentThesis` (017). Successor to the deterministic parts of Ledger's `016-thesis-radar` discovery draft; the free-form LLM "find underhyped trends" idea stays in Ledger.

## Why this spec exists

Denys's stated goal is pursuing opportunities early — "I know MSFT is a strong buy without research; with research and tools, Ledger could leverage this 100×." Two failure modes today: (a) his convictions stay unstructured chat, never becoming monitored theses with invalidation rules; (b) rotation winners are only noticed after the move (the 2026-07-07 lesson). This feature gives both paths one deterministic scorecard and one lifecycle: **candidate → scored → promoted (thesis) | rejected | expired**.

Tier discipline: Finance Sentry computes scores from data; **Ledger interprets** scores, does deep research, writes the narrative, and delivers. No LLM in any scoring path.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Conviction amplification (Priority: P1)

Denys (via Ledger or UI) names a ticker he believes in. The system produces a full deterministic scorecard — structure, fundamentals, crowding, IPS fit — and stores it as a candidate, so intuition becomes evidence-backed and trackable.

**Independent Test**: Call `score_candidate("MSFT", source=user)`; assert a persisted candidate with structure/fundamentals/crowding/composite scores, each with cited inputs, and IPS-fit facts.

**Acceptance Scenarios**:

1. **Given** a ticker with bars (018) and EDGAR fundamentals, **When** `score_candidate` runs, **Then** a candidate is stored with all sub-scores, the composite, and the raw evidence per sub-score (values + periods + windows).
2. **Given** the ticker is already held, **Then** the scorecard includes current position weight and flags concentration vs the IPS.
3. **Given** the ticker has no EDGAR fundamentals (foreign/ETF), **Then** fundamentals sub-score is "not evaluable", the composite is computed from evaluable parts only and labeled partial — never a fake number.
4. **Given** the same ticker is scored again later, **Then** the existing active candidate is re-scored (score history appended), not duplicated.

### User Story 2 — Structure scan generates candidates (Priority: **v2 — deferred**)

> Deferred per 2026-07-07 review: nominating rotation leaders and breakouts from *untuned* 018 thresholds is structurally a chase machine feeding the alert channel. This story ships only after 018's calibration phase (log-only weeks + historical validation) quantifies nomination precision. v1 of this feature is Stories 1 and 3: conviction scoring + promote/reject.

A scheduled scan nominates candidates from market structure: leaders in top-rotating sectors, strong-RS universe members, and new breakouts — each fully scored on creation.

**Independent Test**: Seed bars where sector X ranks top with rising rank delta and member ticker Y has top-decile RS; run `scan_opportunities`; assert Y becomes a `scan`-sourced candidate with a scorecard and a `radar_signals` entry.

**Acceptance Scenarios**:

1. **Given** the scan runs, **Then** every nomination records *why* (the rule that fired: e.g. "top-quartile RS in top-2 rotating sector").
2. **Given** a nominated ticker already has an active candidate or active thesis, **Then** it is re-scored, not duplicated.
3. **Given** nothing qualifies, **Then** the scan records a clean empty run — silence is a valid outcome.
4. **Given** a candidate enters the top tier by composite score, **Then** a `radar_signals` `notable` entry is emitted (and at most one Alert per candidate per silence window).

### User Story 3 — Promote to thesis / reject / expire (Priority: P2)

A candidate Denys accepts is promoted into an `InvestmentThesis` with **proposed invalidation triggers** (017 vocabulary) prefilled deterministically from its fundamentals; rejected candidates keep their scorecard for the track record (020); stale candidates expire.

**Acceptance Scenarios**:

1. **Given** `promote_candidate(id)` with confirmed trigger values, **Then** an `InvestmentThesis` is created (existing `SaveThesis` path, triggers validated per 017 FR-012) and the candidate is linked to it with status `Promoted`.
2. **Given** promotion of a candidate without evaluable fundamentals, **Then** promotion still works but the thesis is flagged "no auto-monitorable triggers" (017 will record it as skipped).
3. **Given** `reject_candidate(id, reason)`, **Then** status is `Rejected` with reason kept — rejected candidates remain queryable for counterfactual tracking (020).
4. **Given** a candidate untouched past the configured TTL (default 30 days), **Then** it auto-expires with a final score snapshot.

### Edge Cases

- Benchmark/sector ETFs themselves are never auto-nominated as candidates (they are lenses, not bets) unless explicitly user-scored.
- Two scan rules nominate the same ticker in one run → one candidate, both reasons recorded.
- 018 data missing (ingestion outage) → scan aborts with a recorded run error; `score_candidate` returns partial scorecard with structure marked not evaluable.
- IPS not configured → IPS-fit section returns "no IPS on file" facts; scoring still completes.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST maintain an `OpportunityCandidate` lifecycle: `Active → Promoted | Rejected | Expired`, with source `User | Scan`.
- **FR-002**: Scoring MUST be deterministic and fully explainable: every sub-score carries the input values, periods, and formula version used. No LLM, no randomness.
- **FR-003**: The **structure sub-score** MUST derive from 018 computations: RS vs benchmark (5/21/63d), sector rank + rank delta of the ticker's sector, breakout state (price vs 63-day high), each normalized to 0–100 by fixed piecewise rules.
- **FR-004**: The **fundamentals sub-score** MUST derive from existing EDGAR fundamentals (017's metric vocabulary): revenue YoY, margin level and trend over the last 4 quarters, EPS YoY — normalized by fixed rules; "not evaluable" when data is missing (never neutral-faked). A filer with no quarterly facts at all (20-F/40-F annual-only filers, #837) is scored on fiscal-year periods instead: YoY and margin trend compare the latest fiscal year with the prior one, and annual YoY off a negative (loss) base is "not evaluable". The evidence records the basis (`FundamentalsBasis`: `quarterly` / `annual`).
- **FR-005**: The **crowding flag** MUST derive from 018's extension + volume-ratio metrics, classifying `Early | Normal | Extended` by configured thresholds.
- **FR-006**: The **IPS-fit section** MUST be factual, not a score: current/would-be position weight vs IPS concentration limits, asset-class fit, and existing sector exposure (via existing allocation queries).
- **FR-006b (expectations section — added 2026-07-08 gap-check)**: The scorecard MUST include a "what's priced in" section: valuation multiples vs the ticker's own history and sector, and the implied growth framing (e.g. "the current P/S requires revenue growth in the top X% of the historical base-rate distribution"). A good business is not automatically a good stock; the thesis must state a variant perception — what the market is missing.
- **FR-006c (base-rate check — added 2026-07-08 gap-check)**: Any growth assumption in a scorecard or promoted thesis MUST be annotated against reference-class base rates (historical distribution of sustained growth at comparable revenue scale; year-to-year earnings-growth persistence is ~zero, so extrapolation is flagged). Deterministic lookup from a small seeded base-rate table; no LLM.
- **FR-007**: There is **no composite single-number score** (removed per 2026-07-07 review: a weighted blend with unbacktested weights is false precision). The scorecard presents sub-scores and evidence side by side; weighing them is Ledger's/Denys's job. Any sub-score that is not evaluable is labeled so.
- **FR-008** *(v2 — deferred, see User Story 2)*: A scheduled scan (Hangfire) nominating candidates by deterministic rules over the 018 universe: (a) top-quartile RS members of top-N rotating sectors, (b) top-decile RS overall, (c) new 63-day-high breakouts with above-average volume. Gated on 018 calibration; rules and thresholds are configuration.
- **FR-009**: `score_candidate` MUST be callable on demand for any ticker (MCP + REST), creating or re-scoring a candidate; score history is append-only.
- **FR-010**: Candidate emissions MUST write to `radar_signals` (018): nomination (`info`), top-tier entry (`notable`), with dedup per candidate per silence window; at most one Alert (existing Alerts module, new `AlertType.Opportunity`) per candidate per window.
- **FR-011**: `promote_candidate` MUST create the `InvestmentThesis` through the existing save path with prefilled proposed triggers that the caller can override; the candidate keeps a link to the thesis. Prefill is an explicit formula, not judgment: (a) `price_drawdown greaterThan <config default 0.30>` for 3 days — always proposed; (b) if revenue YoY is currently positive: `revenue_yoy lessThan 0` for 2 quarters; (c) if gross margin is evaluable: `gross_margin lessThan <latest gross margin − config buffer, default 0.10>` for 2 quarters. Rounded values, cited from the scorecard evidence.
- **FR-011b**: Promotion MUST run the 022 risk-rules check (position sizing, concentration) and refuse — with the violated rule named — when the proposed position would break policy; the caller may override only explicitly.
- **FR-012**: The system MUST expose MCP tools: `scan_opportunities()`, `score_candidate(ticker)`, `list_candidates(status?, source?, minScore?)`, `promote_candidate(id, triggers?)`, `reject_candidate(id, reason)`.
- **FR-013**: Scan runs MUST record a summary (nominated, re-scored, top-tier entries, errors); failures on one ticker never abort the run.
- **FR-014**: The feature MUST NOT execute trades or account actions, and MUST NOT deliver to external channels.

### Key Entities *(data changes)*

- **OpportunityCandidate** *(new)* — `Id, UserId, Ticker, Source, Status, CreatedAt, ExpiresAt, PromotedThesisId?, RejectedReason?, NominationReasons(jsonb)`.
- **CandidateScore** *(new, append-only)* — `CandidateId, ScoredAt, StructureScore?, FundamentalsScore?, CrowdingClass, Evidence(jsonb), FormulaVersion` (no composite column — FR-007).
- **AlertType.Opportunity** *(new const)* — existing Alerts module.
- Writes to **RadarSignal** (018) — no schema change.

### Success Criteria *(mandatory)*

- **SC-001**: Scoring is pure and unit-tested: identical bars + fundamentals + config always produce identical scorecards, including partial/not-evaluable paths.
- **SC-002**: `score_candidate("MSFT")` end-to-end (live 018 data + EDGAR) returns a complete scorecard in < 10s with every sub-score citing its inputs — the "conviction amplification" demo.
- **SC-003** *(v2, with the scan)*: The scheduled scan on a 100-ticker universe completes < 2 minutes and produces zero duplicate candidates across repeated runs.
- **SC-004**: A promoted candidate's thesis is immediately visible to the 017 monitor with valid triggers (contract test across 017/019).
- **SC-005**: Every candidate that ever existed remains queryable with its final score — nothing is deleted (feeds 020 counterfactuals).

## Assumptions & Dependencies

- **Depends on 018** (bars, structure metrics, signal log, universe) and existing Research module (EDGAR fundamentals, theses, watchlist, IPS).
- **Depends on 022** (risk rules) for the promotion-time policy check (FR-011b).
- 017 defines the trigger vocabulary and validation used at promotion.
- Ledger-side: interpretation prompts and Telegram delivery are out of repo scope (see ROADMAP "Ledger-side work").
- Constitution gates apply (zero-warning build, xUnit on scoring core, CQRS, MCP contract test update).

## Notes / Decisions

- **[DECISION]** Two entry paths, one scorecard: user conviction and machine scan produce the same artifact, so 020 can compare their hit rates — measuring whether Denys's intuition or the scan finds better bets.
- **[DECISION]** IPS fit is reported as facts, not folded into the composite — strategy fit is Denys's call (via Ledger), not a hidden weight.
- **[DECISION]** Scoring formulas are versioned (`FormulaVersion`) so historical scores stay honest when weights/rules change.
- **[DECISION 2026-07-08]** FR-006b (expectations/"what's priced in") and FR-006c (base-rate check) are deferred to v1.1 — the scorecard carries reserved null `ValuationContext`/`BaseRateContext` slots, never placeholder data. Recorded here (not only in plan.md) per review: the spec is ground truth.
- **[DECISION 2026-07-08]** No composite score ships (FR-007); consequently `list_candidates` has no `minScore` filter and v1 exposes four MCP tools, not five.
- **[OUT OF SCOPE]** LLM-driven theme discovery (Ledger prompt over `radar_signals`), estimate revisions, flow data, options data, backtesting, any UI beyond MCP/REST in v1.
- **[MCP CONTRACT]** Five new tools (FR-012). Update the MCP tool-count contract test.
