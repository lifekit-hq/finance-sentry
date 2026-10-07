# Feature Specification: Portfolio Construction

**Feature Branch**: `feat/726-portfolio-construction` (build); this spec lands on `fm/fs-726-spec`

**Created**: 2026-10-07

**Status**: Draft. Spec only: no product surface ships during the October consolidation phase. The build
is planned for November.

**GitHub Issue**: #726

**Amends**: [019 Opportunity Scanner](../019-opportunity-scanner/spec.md), FR-007 (scope narrowed; see
[Amendment to 019](#amendment-to-019))

**Also owns**: the 4-axis scorecard visual (fs-825 ruling C3a, 2026-10-05: per-axis strip, designed in this
spec, built after it; #824 hosts it in the dossier header)

## Context

Finance Sentry already judges single names and critiques an existing book:

- The 019 scorecard (`score_candidate`): structure 0–100, fundamentals 0–100, crowding
  `Early | Normal | Extended`, IPS-fit facts and `ScoreEvidence`. It has no composite number (019 FR-007).
- The valuation snapshot (`get_valuation_snapshot`): trailing and forward P/E, EV/EBITDA and dividend yield,
  each against the ticker's own 5-year average, plus consensus target and implied upside.
- The IPS (`get_ips`): goals, horizon, risk, asset-class targets with bands, and exclusions. Allocation drift
  (`get_allocation_vs_target`) measures the book against those targets.
- The 022/039 risk rule set (`check_risk_rules`): position cap, sleeve cap, cash buffer, new-position cap and
  turnover budget.

All of these look **backward** at what the user already holds. A **cold-start user** who holds nothing has
nothing to critique, and no screen ranks a universe at a glance. This feature points the same machinery
**forward**: given an IPS **archetype** (growth, balanced, …) and a scored universe, propose which names to
hold and at what weight. The IPS becomes an input ("here is what to build") as well as a yardstick ("did you
drift?").

The ergonomics come from a competitor's "matrix": a ranked grid of stocks, each with a one-word verdict and a
suggested weight. This spec borrows the at-a-glance read and **rejects the black box**. Every verdict opens
into the facts that produced it.

## Goals

1. **Serve the cold-start user.** A signed-in user with no holdings, no IPS and no risk rule set can pick an
   archetype and get a complete, explained target allocation.
2. **Transparent one-word verdicts.** Each scored name carries one word for scanning. The word is a
   deterministic, documented rendering of the per-axis scorecard. It never replaces the scorecard, and every
   word answers "why?" from the data.
3. **Generic, not the operator's book.** Nothing assumes a particular user's holdings. Archetypes are data
   (configuration), not code.
4. **Reuse before build.** The scorers, valuation read, IPS, allocation drift and risk evaluation are reused
   unchanged. The new logic is the verdict mapping, the archetype definitions, the constructor and the gap
   diff.

## Amendment to 019

| 019 clause | Before                                                                                                                            | After                                                                                                                                                                                                                                                                                                                                                                         |
| ---------- | --------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| FR-007     | No composite single-number score. The scorecard presents sub-scores and evidence side by side; weighing them is the reader's job. | Unchanged for **numbers**: no weighted blend, no 0–100 roll-up and no new opaque score is ever stored or shown. **Added:** a categorical **verdict word** may render the scorecard if (a) it comes from a published, versioned rule table over the existing axis facts, (b) every verdict carries the rules that fired, and (c) the per-axis facts stay on screen next to it. |

Everything else in 019 is unchanged, including the append-only `CandidateScore` history, `FormulaVersion`,
the IPS-fit facts and the promote/risk-gate flow.

## User Scenarios & Testing

### User Story 1 — Ranked universe screen (Priority: P1)

A signed-in user opens one screen that lists every name in the scored universe. Each row shows the ticker, the
four axes (structure, fundamentals, crowding, valuation) and the verdict word. The user can sort and filter by
any axis or by verdict. This is the entry point for a user with no positions.

**Why this priority**: The cold-start user has nothing else to look at. The screen is also the only place
where the verdict and the scorecard visual (US2) are seen across many names at once.

**Independent Test**: Seed a scored universe of three names and sign in as a user with no holdings, no IPS and
no rule set. The screen lists all three, with four axes and a verdict on each row, and "not evaluable" shown
where an axis is null.

**Acceptance Scenarios**:

1. **Given** a user with no holdings, **When** they open the screen, **Then** every universe name is listed
   with its four axes and a verdict, and no row depends on the user's book.
2. **Given** a name whose fundamentals cannot be evaluated (for example a foreign private issuer with too few
   periods, see #837), **When** its row renders, **Then** the fundamentals axis reads "no data" with the
   reason, and the verdict follows the rule table's null-axis rule. It is never computed as if the axis were
   neutral.
3. **Given** the user sorts by structure, **When** the list re-orders, **Then** the order uses the raw
   structure score only. No hidden blended rank exists.
4. **Given** a scorecard older than the staleness window, **When** its row renders, **Then** the row shows the
   score's age and a stale marker.

---

### User Story 2 — Verdict drill-down and the scorecard visual (Priority: P1)

Each verdict expands into the full reasoning: the four axis values, the specific facts behind each axis (from
`ScoreEvidence` and the valuation snapshot), and the rule from the verdict table that fired. The same per-axis
strip renders in the universe screen's drill-down and, through #824, in the asset dossier header.

**Why this priority**: The drill-down is a hard requirement of #726. A verdict without it is the black box this
feature exists to avoid.

**Independent Test**: For a seeded scorecard, the drill-down lists every axis fact, names the fired rule with
its thresholds, and states the verdict-rules version. Re-running the mapping on the displayed facts gives the
same word.

**Acceptance Scenarios**:

1. **Given** any verdict, **When** the user expands it, **Then** they see the four axes, the facts behind
   each, and the fired rule. That is enough to answer "why this word?" with no other screen.
2. **Given** the scorecard visual, **When** it renders, **Then** it is a strip of four per-axis chips:
   - structure as 0–100, with any regime-adjusted value beside the raw one, never replacing it;
   - fundamentals as 0–100 or "no data" with the reason;
   - crowding as `Early`, `Normal` or `Extended`;
   - valuation as its raw facts (for example "P/E 31 vs 5y avg 24, target +12%").

   No radar, filled area or other shape implies a single overall score.

3. **Given** a chip, **When** the user expands it, **Then** it shows that axis's evidence (the RS windows,
   extension and volume ratio for structure; revenue YoY, margin level and trend, and EPS YoY for
   fundamentals).
4. **Given** the verdict rules change version, **When** a stored scorecard is shown, **Then** the drill-down
   names the version that produced its word.

---

### User Story 3 — Construct a target allocation for an archetype (Priority: P1)

The user picks an archetype. The constructor returns a proposed target allocation: a set of names with weights,
the cash and non-equity sleeves, and for every name a one-line reason (which archetype gate it passed, its
verdict, why this weight). The allocation satisfies the archetype's constraints and any limits the user's IPS
and risk rule set impose.

**Why this priority**: This is the new capability. It is the reason a cold-start user can start at all.

**Independent Test**: For a fixed seeded universe and the "growth" archetype, the proposal is identical across
runs. Weights sum to 100% including cash, no name exceeds the effective position cap, and every name has a
reason.

**Acceptance Scenarios**:

1. **Given** a cold-start user (no IPS, no rule set) and the "growth" archetype, **When** they construct,
   **Then** the proposal uses the archetype's default constraints and labels each constraint's source as
   "archetype default".
2. **Given** a user whose risk rule set sets `MaxPositionWeightPct` to 8%, **When** they construct, **Then** no
   name exceeds 8%, and the constraint is labelled "your risk rules". The stricter of the rule set and the
   archetype default wins.
3. **Given** an IPS whose `Exclusions` list a ticker or sector, **When** they construct, **Then** no excluded
   name appears, and the screen says it was excluded and why.
4. **Given** too few eligible names to fill the equity sleeve under the position cap, **When** they construct,
   **Then** the remainder goes to cash with an explicit note. The cap is never breached to fill the sleeve.
5. **Given** a proposal, **When** it is produced, **Then** the proposed book has been evaluated by the existing
   risk evaluation, and a proposal that violates a rule is never shown as compliant.
6. **Given** any proposal, **Then** no trade, order or account action is created. Acting on a name goes
   through the existing promote and risk-gate flow (019 FR-011/FR-011b).

---

### User Story 4 — Gap mode for existing holders (Priority: P2)

A user who already holds positions sees the difference between the current book and the archetype's
constructed target: names missing, names overweight or underweight, held names outside the target, and
sleeve-level drift. Each line carries the size of the gap in weight points and as an amount (USD book
figures, displayed in the profile base currency the way the dashboard displays them).

**Why this priority**: It reuses US3's output for the existing user, but the cold-start path (US1–US3) is
complete without it.

**Independent Test**: For a seeded book of two names and a growth target of four, gap mode lists the two
missing names, the over- or under-weight on the held ones, and the cash difference. The current weights match
`get_allocation_vs_target` for the same book.

**Acceptance Scenarios**:

1. **Given** a held name not in the target, **When** gap mode renders, **Then** the name is listed as "held,
   not in target" with its verdict. Nothing tells the user to sell it.
2. **Given** a multi-currency book, **When** gap mode computes current weights, **Then** it uses the
   base-currency-converted figures of the existing book read, never native amounts summed across currencies.
3. **Given** a user with no holdings, **When** they open gap mode, **Then** they are routed to US3. Every
   target name would be "missing", so a gap view adds nothing.

---

### Edge Cases

- **Every axis null**: the verdict is `NOT SCORED`, not `AVOID`, and the name is never eligible for
  construction.
- **Fundamentals null on a name otherwise strong**: the rule table decides. The default rule caps the
  verdict one step below the top word, so a missing axis is never read as a passing one.
- **Valuation not applicable** (crypto, a fund): the name is outside the v1 universe, which is single
  equities only.
- **Stale score or stale quote**: the stale marker shows. A name staler than the construction window is
  ineligible for construction but still listed on the screen.
- **Scoring failed for one name**: the row shows the failure reason. The rest of the universe and the
  construction still render.
- **Universe empty** (the scan shortlist failed and no curated list is configured): the screen shows an
  explicit empty state, and construction refuses with a reason.
- **IPS targets an asset class the universe cannot fill** (bonds, real estate): the sleeve appears at its
  target weight with no names and a note "not built from the universe in v1".
- **IPS sleeve targets do not sum to 100%**: allocation drift (`GetAllocationDriftQuery`) does not normalise
  targets today, so construction needs its own rule. See OQ-10.
- **Two archetypes give the same names**: allowed, since the weights and reasons still differ by archetype.

## Requirements

### Functional Requirements

**Universe and scoring**

- **FR-001**: The scored universe MUST be shared across users (one scorecard per symbol per scoring run) and
  MUST NOT depend on any user's holdings. Per-user facts (IPS fit, held weight) are computed at read time for
  the viewing user.
- **FR-002**: Universe scorecards MUST be produced by the existing 019 scorers, unchanged:
  `StructureScorer`, `FundamentalsScorer` and `CrowdingClassifier`. They carry the same `ScoreEvidence` and
  `FormulaVersion`. No parallel scoring formula may exist.
- **FR-003**: The universe MUST be bounded and MUST respect the broad-universe-scan rule that structure is
  never computed over the full constituent list (spec `20260910-001634-broad-universe-scan` FR-011). Its
  composition is Open question OQ-1.
- **FR-004**: The valuation axis MUST come from the existing valuation snapshot and MUST be shown as raw facts.
  No 0–100 valuation score may be introduced.

**Verdict**

- **FR-005**: Each scorecard MUST map to exactly one verdict word through a deterministic, versioned rule table
  over the four axes. The same inputs and the same rules version MUST always give the same word.
- **FR-006**: The rule table MUST be published (in the drill-down payload and in this spec's verdict table once
  OQ-3 and OQ-4 are settled). Each verdict MUST carry the rules version and the rule that fired.
- **FR-007**: The verdict MUST NOT be stored or exposed as a number. No composite, weighted or rank score may be
  added anywhere (019 FR-007 as amended).
- **FR-008**: A null axis MUST be handled by an explicit rule. It is never treated as a mid value.

**Scorecard visual (C3a)**

- **FR-009**: The scorecard MUST render as a per-axis strip of four chips, each expandable to its evidence. A
  radar, filled polygon or any single-shape summary is out of bounds.
- **FR-010**: The strip MUST be one shared component, used by the universe screen drill-down and offered to
  #824 for the dossier header. Any new primitive it needs MUST land in lifekit-common first.
- **FR-011**: Structure MUST show the raw score. A regime-adjusted value, when present, MUST appear beside the
  raw one and never replace it.

**Archetypes and construction**

- **FR-012**: Archetypes MUST be defined as configuration: an eligibility gate per axis, an ordering, and
  default constraints (position cap, sleeve cap, cash floor, name count). Adding an archetype MUST NOT need a
  code change.
- **FR-013**: The constructor MUST be deterministic and rules-based (OQ-5). Given the same universe snapshot,
  archetype, IPS and rule set, it MUST return the same allocation.
- **FR-014**: The effective constraint for each limit MUST be the stricter of the user's risk rule set and the
  archetype default. The source of every applied limit MUST be labelled in the output.
- **FR-015**: IPS `Exclusions` MUST be honoured. IPS `AllocationTargets` MUST set the sleeve weights when an
  IPS exists; otherwise the archetype's default sleeves apply.
- **FR-016**: Every proposed name MUST carry a reason: the gate it passed, its verdict, and how its weight was
  set (for example "equal weight, capped at 8% by your risk rules").
- **FR-017**: Before it is returned, the proposed book MUST be evaluated by the existing Risk evaluation (the
  same evaluation `check_risk_rules` uses). Any violation MUST be reported on the proposal. A violating
  proposal is never presented as compliant.
- **FR-018**: Construction MUST NOT create trades, orders, candidates, theses or IPS versions. Acting goes
  through the existing promote and risk-gate flow.

**Gap mode**

- **FR-019**: Gap mode MUST diff the current book against the constructed target per name and per sleeve,
  using the existing base-currency book figures. It MUST NOT sum native amounts across currencies (see
  `docs/money-semantics.md`).
- **FR-020**: Gap lines MUST be descriptive ("missing", "overweight by 3.1 pts") and MUST NOT be phrased as an
  instruction to buy or sell.

**Surfaces and boundaries**

- **FR-021**: Every read MUST be exposed as an authenticated REST endpoint and as an MCP tool: the universe
  screen, a single scorecard with its verdict, construction and gap mode. Names follow the domain concept,
  never a consumer (constitution VII.3). The MCP tool-count contract test is updated in the same PR.
- **FR-022**: Per-user state (the selected archetype, if persisted per OQ-7) MUST be owner-scoped with the
  named owner query filter and ship an isolation test. Shared universe scorecards follow the shared-corpus
  pattern (`UserId == null` visible to all).
- **FR-023**: The feature MUST be config-gated (off by default) so it can merge before it is announced.

### Key Entities

- **UniverseMember** _(new)_: a symbol in the scored universe, with its membership source (scan shortlist,
  curated list, held, watchlist) and when it joined.
- **UniverseScorecard** _(new, shared corpus, append-only)_: symbol, scored at, structure, fundamentals,
  crowding, `ScoreEvidence`, `FormulaVersion`, plus a reference to the valuation snapshot used. It mirrors
  `CandidateScore` without `CandidateId` or `IpsFit`, both of which are per-user.
- **Verdict** _(new, computed, never stored as a number)_: word, rules version, fired rule id, and the axis facts
  the rule read.
- **Archetype** _(new, configuration)_: key, label, per-axis gates, ordering, and default constraints.
- **ConstructionProposal** _(new, computed)_: archetype, applied constraints (each with its source), sleeves,
  names with weight and reason, cash, risk evaluation result, and the universe snapshot time.
- **GapReport** _(new, computed)_: per-name and per-sleeve lines (missing, overweight, underweight, held not in
  target) with weight points and amount.

## Success Criteria

### Measurable Outcomes

- **SC-001**: A user with no holdings, no IPS and no rule set gets a complete proposal with reasons for every
  name, verified by an end-to-end test against a seeded universe.
- **SC-002**: For every verdict in a seeded fixture, a test re-derives the word from the displayed facts and
  rules version, and the result matches. Coverage is 100% of the rules.
- **SC-003**: No API response, MCP payload or table contains a composite or weighted score field. A contract
  test asserts the response shapes.
- **SC-004**: Construction is deterministic (same inputs give a byte-identical proposal), and no proposed name
  exceeds the effective position cap (property test over randomised universes).
- **SC-005**: Gap-mode current weights equal `get_allocation_vs_target`'s for the same multi-currency book.
- **SC-006**: The scorecard strip has screenshot baselines at 390 px and desktop (the #467 rails) when it ships.

## Open Questions

Each question below is a product choice #726 leaves open. The recommendation is the default the build follows
unless it is overruled before November.

| #     | Question                                                                                       | Options                                                                                                                                    | Recommended                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                     |
| ----- | ---------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| OQ-1  | How is the universe defined?                                                                   | (a) fixed curated list; (b) index-derived (S&P 500); (c) user-configurable                                                                 | **(a) plus the existing feeds**: the broad-universe-scan stage-1 shortlist (bounded by its cap) ∪ a configured curated list ∪ the viewer's held and watchlist names. The full S&P 500 is reached only through the shortlist pre-filter, so structure is never computed index-wide (scan FR-011). User curation comes later and already partly exists through the watchlist.                                                                                                                                                                                                                                     |
| OQ-2  | Which archetypes ship in v1?                                                                   | (a) growth + dividend + balanced; (b) growth + balanced, dividend next                                                                     | **(b).** A dividend archetype needs payout stability, and nothing reads dividend history today: the valuation snapshot has current yield with `HistoryUnavailable`, and fundamentals has no dividend concept. A yield-only income screen would steer a cold-start user toward yield traps. Dividend is the first follow-up once a dividend-history read exists. Value follows with the 019 v1.1 "what's priced in" section.                                                                                                                                                                                     |
| OQ-3  | What is the verdict vocabulary?                                                                | (a) the competitor's (`STRONG BUY / BUY / WAIT / AVOID`); (b) our own scale                                                                | **(b): `STRONG / FAVOURABLE / WAIT / AVOID`, plus the non-verdict `NOT SCORED`.** The product is generic and multi-user and never places trades (019 FR-014). The word describes the evidence, not an instruction, so it carries no "buy". The four-step order keeps the at-a-glance read.                                                                                                                                                                                                                                                                                                                      |
| OQ-4  | What shape does the verdict rule table take?                                                   | (a) an ordered decision list: first matching rule wins, each rule a conjunction of axis thresholds; (b) points per axis, summed into bands | **(a).** (b) is a composite score under another name and breaks 019 FR-007. Draft rules for calibration: `AVOID` if structure < 30 or (crowding `Extended` and valuation above its own 5y average); `STRONG` if structure ≥ 70, fundamentals ≥ 70, crowding not `Extended`, and valuation not above its 5y average by more than the configured premium; `FAVOURABLE` if structure ≥ 50 and fundamentals ≥ 50 (or null, capped here per FR-008); otherwise `WAIT`. Thresholds are configuration with a rules version, and the final table is fixed by a calibration task against stored scorecards before build. |
| OQ-5  | Is the constructor rules-based or an optimiser?                                                | (a) rules-based tilts; (b) an optimiser (mean-variance or similar)                                                                         | **(a) for v1.** Gate by archetype, order by the archetype's axis keys (lexicographic, never a weighted sum, per OQ-4), take the top N, then equal-weight them, capped and with the remainder to cash. Every weight is explainable in one line. An optimiser needs covariance data and return estimates the platform does not hold, and its output is not explainable per name.                                                                                                                                                                                                                                  |
| OQ-6  | How are weights set inside the equity sleeve?                                                  | (a) equal weight; (b) tiered by verdict (`STRONG` > `FAVOURABLE`); (c) inverse volatility                                                  | **(a).** It is the simplest rule a user can check by hand. Tiering by verdict turns the word into a sizing input and comes close to a composite. Inverse volatility needs bars, which only shortlist, held and watchlist names have.                                                                                                                                                                                                                                                                                                                                                                            |
| OQ-7  | Is the selected archetype stored, and where?                                                   | (a) not stored, passed per request; (b) stored per user in the new module; (c) a new field on the IPS                                      | **(b).** Gap mode and the dashboard need a remembered choice. (c) changes the IPS schema and internals, which #726 rules out. An IPS field can follow if the archetype proves to be policy rather than preference.                                                                                                                                                                                                                                                                                                                                                                                              |
| OQ-8  | What do the verdict and construction read for structure: the raw or the regime-adjusted score? | (a) raw; (b) regime-adjusted                                                                                                               | **(a) raw.** It is stable and auditable, matching how 021 keeps the raw score authoritative. The regime adjustment is shown beside it (FR-011).                                                                                                                                                                                                                                                                                                                                                                                                                                                                 |
| OQ-9  | Does choosing an archetype write an IPS for a cold-start user?                                 | (a) yes, seed an IPS version; (b) no, construction reads archetype defaults                                                                | **(b).** Authoring an IPS stays the deliberate `save_ips` flow. The proposal shows "archetype default" as the source of each limit, which invites the user to write their own.                                                                                                                                                                                                                                                                                                                                                                                                                                  |
| OQ-10 | What happens when IPS sleeve targets do not sum to 100%?                                       | (a) scale them proportionally to 100%; (b) put the shortfall in cash; (c) refuse to construct                                              | **(b).** It never invents exposure the IPS did not ask for, and the proposal names the unallocated points as "cash: IPS targets sum to N%". An overshoot (over 100%) refuses with a reason, because the stated policy cannot be built as written.                                                                                                                                                                                                                                                                                                                                                               |

## Out of Scope

- Placing trades, orders or account actions, or creating candidates, theses or IPS versions from a proposal.
- An optimiser, backtesting of archetypes, or expected-return figures.
- Any composite or weighted score, letter grade or radar or snowflake visual (fs-825 C3a rejected the radar).
- Non-equity sleeves built from named instruments (bonds, funds, crypto). These are sleeve-level only in v1.
- The dividend and value archetypes in v1 (OQ-2).
- The Finviz-style heatmap (fs-825 C4a: reference only).

## Assumptions

- The October consolidation phase rules out new product surface. This document is the only deliverable now,
  and implementation starts in November.
- The 019 scorers, the valuation snapshot, the IPS, allocation drift and the Risk evaluation behave as they do
  on main at 2026-10-07. [plan.md](./plan.md) lists the exact files that are reused unchanged.
- #837 (FPI fundamentals) keeps reducing null fundamentals, and this feature renders null-with-reason either
  way.
- Archetype default constraints are illustrative until calibrated. They are configuration, and the build
  ships them with a documented source (for example a common retail position-cap convention), never as advice.
