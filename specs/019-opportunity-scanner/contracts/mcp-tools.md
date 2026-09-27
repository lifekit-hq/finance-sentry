# MCP Tool Contracts: Opportunity Scanner (v1 — 4 tools)

All: `[McpServerToolType]`, primary-ctor DI of a CQRS handler + `IIdentityResolver`,
`[McpServerTool(Name=…)]` + `[Description]`, resolve `userId ?? identity.GetUserId()`.
Allowlist 43 → 47; register injected deps (`IMarketStructureReader`, `IRiskPolicyGate`, candidate
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

## `promote_candidate`
- **Params**: `id: Guid`, `triggers?: ThesisInvalidationTrigger[]` (override the deterministic prefill),
  `overrideRisk?: bool`, `proposedUsd?: decimal`, `decisionNote?: string`, `userId?`, `paper?: bool`
  (finance-sentry#704, default `false`)
- **Returns**: `{ thesisId?, gate: RiskGateVerdict }`. Runs `IRiskPolicyGate.CheckProposalAsync`
  (FR-011b): `Refused` (and no `overrideRisk`) → returns the verdict with the named rule + max compliant
  size, NO thesis created. Otherwise creates the `InvestmentThesis` via `SaveThesisCommand` with prefilled
  triggers (validated by `ThesisTriggerVocabulary`), links the candidate (`PromotedThesisId`, status
  `Promoted`), records a `Promoted` candidate event (020). An override is recorded as a signal (FR-007).
- **`paper` (finance-sentry#704)**: a paper/tracking-only promotion never draws down real cash, so the
  real-book cash-funding rule (`MinCashBuffer`) has no meaning for it and is skipped when `paper: true`.
  Concentration/sizing rules (`MaxPositionWeight`, `MaxNewPosition`, `Turnover`) still apply unchanged —
  only the cash-funding rule is the mismatch (per the issue's option (b)). `paper: false` (the default)
  keeps today's full real-book rule set, unchanged.
- **Acceptance (SC-004)**: the promoted thesis is immediately valid for the 017 monitor.

## `reject_candidate`
- **Params**: `id: Guid`, `reason: string`, `userId?`
- **Returns**: updated candidate (status `Rejected`, reason kept). Records a `Rejected` event; remains
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
