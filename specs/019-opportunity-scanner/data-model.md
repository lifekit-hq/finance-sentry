# Phase 1 Data Model: Opportunity Scanner

Two new tables in `ResearchDbContext` (schema `research`), migration **M006_OpportunityCandidates**.
Scoring DTOs are in-memory. Two new Core DTOs cross module boundaries.

## Table: `opportunity_candidates`

| Field | Type | Notes |
|---|---|---|
| `Id` | Guid | PK, gen_random_uuid() |
| `UserId` | Guid | user-scoped |
| `Ticker` | string | upper-invariant |
| `Source` | string enum | `User` \| `Scan` (Scan unused in v1) |
| `Status` | string enum | `Active` \| `Promoted` \| `Rejected` \| `Expired` |
| `CreatedAt` | DateTimeOffset | |
| `ExpiresAt` | DateTimeOffset | CreatedAt + TTL (config default 30d) |
| `PromotedThesisId` | Guid? | set on promote (links to InvestmentThesis) |
| `RejectedReason` | string? | set on reject |
| `NominationReasons` | jsonb | list of reasons (why nominated; user-source = ["conviction"]) |

**Indexes**: `(UserId, Status)`, `(UserId, Ticker)` (re-score lookup), and unique partial
`ux_opportunity_candidates_user_ticker_active` on `(UserId, Ticker)` `WHERE Status = 'Active'` (M020).
**Invariant**: one `Active` candidate per `(UserId, Ticker)` — re-score appends a `CandidateScore`, never a
duplicate candidate (FR US1.4). `CandidateRepository.UpsertActiveAsync` checks first; the unique partial index
settles a concurrent insert race (the loser re-reads and returns the winner's row).

## Table: `candidate_scores` (append-only)

| Field | Type | Notes |
|---|---|---|
| `Id` | Guid | PK |
| `CandidateId` | Guid | FK-by-convention |
| `ScoredAt` | DateTimeOffset | |
| `StructureScore` | int? | 0–100; null = not evaluable |
| `FundamentalsScore` | int? | 0–100; null = not evaluable |
| `CrowdingClass` | string enum | `Early` \| `Normal` \| `Extended` |
| `IpsFit` | jsonb | facts: currentWeight, wouldBeWeight, maxSinglePositionPct, assetClassFit, sectorExposure, flags |
| `Evidence` | jsonb | per-sub-score raw inputs/periods/windows + reserved slot for FR-006b/c |
| `FormulaVersion` | int | bump when normalization rules change (FR-002 honesty) |

**No composite column** (FR-007). Append-only — re-score adds a row.

## Enums (Research `Domain/Opportunity`)
- `CandidateSource`: `User`, `Scan`
- `CandidateStatus`: `Active`, `Promoted`, `Rejected`, `Expired`
- `CrowdingClass`: `Early`, `Normal`, `Extended`

## New Core DTOs + interfaces

### `IMarketStructureReader` (Core; impl in Radar)
`GetStructureAsync(string ticker, CancellationToken) → MarketStructureSnapshot?`
`MarketStructureSnapshot(string Ticker, IReadOnlyDictionary<int,decimal?> RsByWindow,
IReadOnlyDictionary<int,decimal?> ReturnByWindow, decimal? ExtensionFromMa50, decimal? TodayZScore,
decimal? VolumeRatio, decimal? Ma50, decimal? Ma200, bool Stale)` — projection of Radar's `TickerStructure`.

### `IRiskPolicyGate` (Core; impl in Risk)
`CheckProposalAsync(Guid userId, string ticker, decimal proposedUsd, bool overrideFlag, CancellationToken)
→ RiskGateVerdict`
`RiskGateVerdict(RiskGateDecision Decision, string? RuleKey, decimal? ObservedValue, decimal? LimitValue,
decimal? MaxCompliantSizeUsd, string? Note)` with `RiskGateDecision { Allowed, Refused }` — projection of
Risk's `RiskVerdict`. `HasRuleSet=false` → treat as Allowed with a "no rules on file" note (never blocks).

## In-memory scoring DTOs (`Domain/Scoring`)
- `CandidateScorecard(int? StructureScore, int? FundamentalsScore, CrowdingClass Crowding,
  IpsFitFacts IpsFit, ScoreEvidence Evidence, int FormulaVersion)`
- `IpsFitFacts(decimal? CurrentWeight, decimal? WouldBeWeight, decimal? MaxSinglePositionPct,
  bool WithinConcentration, string AssetClassFit, string? Note)`
- `ScoreEvidence` — structured per-section inputs (RS values by window, margins by quarter, YoY, z-score,
  volume ratio, breakout distance) for 100% explainability (SC-001/FR-002).
- `ProposedTrigger` list from `TriggerPrefill` → mapped to `ThesisInvalidationTrigger` at promotion.

## MCP tool contracts (v1 — 4 tools; see contracts/)
| Tool | Params | Returns |
|---|---|---|
| `score_candidate` | `ticker`, `decisionNote?`, `userId?` | full scorecard (creates/re-scores candidate) |
| `list_candidates` | `status?`, `source?`, `userId?` | candidates + latest score |
| `promote_candidate` | `id`, `triggers?` (override prefill), `overrideRisk?`, `userId?` | thesis id + gate verdict |
| `reject_candidate` | `id`, `reason`, `userId?` | updated candidate |

`scan_opportunities` (5th tool) ships with US2 in v2.

## Migration M006_OpportunityCandidates
EF `CreateTable` for both tables (correct PascalCase — no raw SQL). jsonb columns
(`NominationReasons`, `IpsFit`, `Evidence`) via `HasConversion` (Web JSON) **with a ValueComparer**
(learned from 018's RadarSignal.Payload EF warning). Enum columns as `HasConversion<string>().HasMaxLength(20)`.
