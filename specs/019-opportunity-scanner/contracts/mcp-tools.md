# MCP Tool Contracts: Opportunity Scanner (v1 — 4 tools)

All: `[McpServerToolType]`, primary-ctor DI of a CQRS handler + `IIdentityResolver`,
`[McpServerTool(Name=…)]` + `[Description]`, resolve `userId ?? identity.GetUserId()`.
Allowlist 43 → 47 (plus `revert_thesis_revision` for Ledger-made revisions); register injected deps (`IMarketStructureReader`, `IRiskPolicyGate`, candidate
repos, scorers) in `ToolParityTests`.

## `score_candidate`
- **Params**: `ticker: string`, `decisionNote?: string`, `userId?: Guid`
- **Returns**: full scorecard — `structureScore?`, `fundamentalsScore?`, `crowdingClass`, `ipsFit` facts,
  `evidence` (raw inputs per sub-score), `formulaVersion`. **No composite score.** Creates a candidate
  (source `User`) or re-scores the existing active one (appends a `CandidateScore`). Not-evaluable
  sub-scores are `null` + labeled; partial scorecards are marked, never faked (US1.3).
- **Acceptance (SC-002)**: MSFT with live 018 structure + EDGAR returns a complete scorecard < 10s,
  every sub-score citing its inputs.

## `list_candidates`
- **Params**: `status?` (Active|Promoted|Rejected|Expired), `source?` (User|Scan), `userId?`
- **Returns**: candidates with their latest score + status/links. Rejected/expired remain listed (SC-005).

## Authorization and origin (owner ruling 2026-11-13)
`promote_candidate`, `reject_candidate` and `save_thesis` are open to the service token (Ledger): the owner's only
decision is buy or not, so Ledger nominates, promotes and rejects without a confirm step. The write is stamped
`origin` (`Ledger` for a service token, `User` for the owner's session and stdio) on its `thesis_events` row
(`ThesisEvent.Origin`, migration M024), and `list_candidates` shows it as `decisionOrigin` (the origin of the
latest `Promoted`/`Rejected` event; `User` while Active). The risk gate, trigger validation and the one-thesis rules
are unchanged for every caller. `delete_thesis` and everything on the IPS stay owner-session only. A promoted
thesis's own `Created` event carries the audit payload (see docs/mcp.md); the `Promoted` event carries the
`decisionNote` as `audit.rationale`. Pinned by `ResearchWriteAuthorizationTests`.

## `promote_candidate`
- **Params**: `id: Guid`, `triggers?: ThesisInvalidationTrigger[]` (override the deterministic prefill),
  `overrideRisk?: bool`, `proposedUsd?: decimal`, `decisionNote?: string`, `userId?`, `paper?: bool`
  (finance-sentry#704, default `false`), `evidence?: { newsArticleIds?, filingAccessions?, eventIds?, fundamentalsAsOf? }`
  (kept in the audit payload)
- **Evidence at entry**: the created thesis carries the candidate's latest scorecard summary (structure /
  fundamentals scores, crowding class, scored date) and the `decisionNote` in `ThesisText`, and the
  scorecard's observed figures (structure/fundamentals score, revenue YoY, gross margin, EPS YoY) as
  `KeyDataPoints` (`direction: "observed"`, `source: "candidate_scorecard"`). A candidate with no scorecard degrades to the
  default text and no data points; it never fails the promotion. Triggers, risk gate and the
  `SaveThesisCommand` path are unchanged; no schema change.
- **Returns**: `{ thesisId?, gate: RiskGateVerdict, note? }`. Runs `IRiskPolicyGate.CheckProposalAsync`
  (FR-011b): `Refused` (and no `overrideRisk`) → returns the verdict with the named rule + max compliant
  size, NO thesis created. Otherwise creates the `InvestmentThesis` via `SaveThesisCommand` with prefilled
  triggers (validated by `ThesisTriggerVocabulary`), links the candidate (`PromotedThesisId`, status
  `Promoted`), records a `Promoted` candidate event (020). An override is recorded as a signal (FR-007). `overrideRisk` is
  owner-only: a Ledger (service-token) call ignores it, stays `Refused`, and `note` says the override is owner-only.
- **`paper` (finance-sentry#704)**: a paper/tracking-only promotion never draws down real cash, so the
  real-book cash-funding rule (`MinCashBuffer`) has no meaning for it and is skipped when `paper: true`.
  Concentration/sizing rules (`MaxPositionWeight`, `MaxNewPosition`, `Turnover`) still apply unchanged —
  only the cash-funding rule is the mismatch (per the issue's option (b)). `paper: false` (the default)
  keeps today's full real-book rule set, unchanged.
- **Acceptance (SC-004)**: the promoted thesis is immediately valid for the 017 monitor.

## `reject_candidate`
- **Params**: `id: Guid`, `reason: string`, `userId?`
- **Returns**: updated candidate (status `Rejected`, reason kept). Records a `Rejected` event (with `origin`, `audit.rationale` = the reason); remains
  queryable for counterfactuals (SC-004/020).

## Cross-module contracts (not MCP)
- **`IMarketStructureReader`** (Core; Radar impl) — read 018 structure for a ticker.
- **`IRiskPolicyGate`** (Core; Risk impl) — promote-time policy check.
- Emits via existing `IRadarSignalWriter` (Scanner `opportunity`): `info` nomination, `notable` top-tier.
- Alerts via `IAlertGeneratorService.GenerateOpportunityAlertAsync` (new `AlertType.Opportunity`),
  ≤1 per candidate per silence window.

## Deferred
- `scan_opportunities()` — US2 machine scan, v2 (gated on 018 calibration). 5th tool ships then.

## Behaviour contracts
- Determinism (SC-001): identical bars + fundamentals + config → identical scorecard, incl. partial paths.
- No LLM / randomness in any scoring path (FR-002). No trades, no channel delivery (FR-014).
