# Implementation Plan: Portfolio Construction

**Branch**: `feat/726-portfolio-construction` (November build) | **Date**: 2026-10-07 | **Spec**: [spec.md](./spec.md)

## Summary

Add a shared, bounded **universe scorecard** produced by the existing 019 scorers, a deterministic **verdict
rule table** over the four axes, a per-axis **scorecard strip** (fs-825 C3a), and a new
**PortfolioConstruction** module. That module turns an archetype plus the user's IPS and risk limits into a
target allocation and a gap report. The module consumes Research, Risk and Core through published ports and
leaves their existing logic untouched.

This plan fixes the shape of the November build. It is not a task list: `tasks.md` is generated when the open
questions in the spec are settled.

## Technical Context

**Language/Version**: .NET 10 / C# 14 (backend), Angular 21.2 / TypeScript strict (frontend)

**Primary Dependencies**: ASP.NET Core, EF Core, hand-rolled CQRS (`FinanceSentry.Core.Cqrs`), Hangfire,
Options binding, NgRx SignalStore, `@lifekit-hq/ui`

**Storage**: PostgreSQL. New tables for universe members and scorecards (Research schema, shared corpus) and
the archetype as a new IPS field (spec OQ-7); the new module needs no per-user table of its own

**Testing**: xUnit unit tests for the verdict rules and the constructor (pure functions), contract tests via
`WebApplicationFactory`, MCP contract and tool-count tests, Vitest for the store and utils, Playwright
screenshot baselines (#467 rails)

**Constraints**: zero build warnings, ESLint zero-error, lifekit-common-first for any new UI primitive, no
composite score anywhere (spec FR-007), config-gated and off by default (spec FR-023)

## Constitution Check

- **I. Modular monolith**: new self-registering module (`IModuleRegistrar`). Cross-module reads go through
  published ports with Integration adapters only, and no module references another's internals. ✓
- **II. Code quality**: pure, unit-tested rule and constructor functions; zero warnings. ✓
- **IV. AI-driven analytics**: deterministic, with no LLM in scoring, verdict or construction (as 019 FR-002). ✓
- **VI. Frontend discipline**: signal store per page, `cmn-*` components, and a library-first strip. ✓
- **VII. Ledger boundary**: MCP tools mirror the REST reads and are named for the domain, not the consumer. ✓

## Reuse Map

### Reused unchanged

| Capability                                    | File(s)                                                                                                                                                                                    | Used for                                            |
| --------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | --------------------------------------------------- |
| Structure sub-score                           | `backend/src/FinanceSentry.Modules.Research/Domain/Scoring/StructureScorer.cs`                                                                                                             | universe scorecard structure axis                   |
| Fundamentals sub-score                        | `backend/src/FinanceSentry.Modules.Research/Domain/Scoring/FundamentalsScorer.cs`                                                                                                          | fundamentals axis                                   |
| Crowding class                                | `backend/src/FinanceSentry.Modules.Research/Domain/Scoring/CrowdingClassifier.cs`                                                                                                          | crowding axis                                       |
| Evidence and formula version                  | `backend/src/FinanceSentry.Modules.Research/Domain/Scoring/CandidateScorecard.cs` (`ScoreEvidence`, `FormulaVersion`, `RegimeAdjustment`)                                                  | drill-down facts; regime shown beside raw           |
| Market structure and EDGAR inputs             | `IMarketStructureReader`, `ISecEdgarService` (inputs to `ScoreCandidateCommandHandler` in `Application/Commands/ScoreCandidateCommand.cs`)                                                 | inputs to the scorers                               |
| Valuation snapshot (`get_valuation_snapshot`) | `backend/src/FinanceSentry.Modules.Research/Application/Queries/GetValuationSnapshotQuery.cs`, `API/Responses/ValuationSnapshotResult.cs`                                                  | valuation axis as raw facts                         |
| Universe feed                                 | broad-universe-scan stage 1: `Domain/Scoring/ScanShortlistRules.cs`, `Infrastructure/Jobs/OpportunityScanJob.cs`, `Infrastructure/Resources/sp500-constituents.json`                       | universe membership (spec OQ-1)                     |
| IPS (`get_ips`)                               | `backend/src/FinanceSentry.Modules.Research/Domain/InvestmentPolicyStatement.cs`, `Application/Queries/GetIpsQuery.cs`                                                                     | `AllocationTargets`, `Exclusions`                   |
| Allocation drift (`get_allocation_vs_target`) | `backend/src/FinanceSentry.Modules.Research/Domain/Ports/IAllocationDriftReader.cs`, `Application/Queries/GetAllocationDriftQuery.cs`                                                      | gap mode's sleeve-level current weights             |
| Book figures                                  | `backend/src/FinanceSentry.Core/Interfaces/IBookFiguresService.cs`, `Core/Services/BookFiguresService.cs`                                                                                  | gap mode's per-name current weights in USD          |
| Asset-class buckets                           | `backend/src/FinanceSentry.Core/Domain/AssetClassNormalizer.cs`                                                                                                                            | sleeve matching                                     |
| Risk limits                                   | `backend/src/FinanceSentry.Modules.Risk/Domain/Ports/IRiskLimitsReader.cs` (`RiskLimits`)                                                                                                  | effective position cap and cash floor (spec FR-014) |
| Risk rule set (`check_risk_rules`)            | `backend/src/FinanceSentry.Modules.Risk/Domain/RiskRuleSet.cs`, `Application/Services/RiskEvaluationService.cs` (`IRiskEvaluationService.Evaluate`, a pure function over a `BookSnapshot`) | evaluating the proposed book (spec FR-017)          |
| Promote and risk gate                         | `promote_candidate` / `check_risk_rules` (019 FR-011/FR-011b)                                                                                                                              | the only path from a proposal to an action          |

### New (additive to existing modules)

| Item                                                       | Where                                                        | Why there                                                                                                                                                        |
| ---------------------------------------------------------- | ------------------------------------------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `UniverseMember`, `UniverseScorecard` entities + migration | Research (`Domain/Scoring/`, shared corpus `UserId == null`) | the scorers and their inputs live in Research; the issue puts verdict thresholds "with the scoring module"                                                       |
| `UniverseScoringJob` (Hangfire)                            | Research `Infrastructure/Jobs/`                              | calls the unchanged scorers per member and appends scorecards; without `ScoreCandidateCommandHandler`'s per-user side effects (no candidates, signals or alerts) |
| `VerdictRules` (pure, reads the regime-adjusted structure score per spec OQ-8) + `VerdictOptions`                   | Research `Domain/Scoring/`                                   | the versioned decision list (spec OQ-4); thresholds via Options binding                                                                                          |
| `IUniverseScorecardReader` published port                  | Research `Domain/Ports/`                                     | same pattern as `IAllocationDriftReader`                                                                                                                         |
| `IProposedBookEvaluator` published port                    | Risk `Domain/Ports/`                                         | wraps the unchanged `IRiskEvaluationService.Evaluate` for a hypothetical `BookSnapshot`; Risk logic untouched                                                    |
| IPS `Archetype` field + migration                          | Research IPS (`save_ips` / `get_ips`)                        | spec OQ-7: the one deliberate IPS schema change; Research logic otherwise untouched                                                                              |
| Scorecard HTTP read                                        | Research API                                                 | today the scorecard is MCP-only (fs-825 §3.2); needed by the strip and #824                                                                                      |

### New module: `FinanceSentry.Modules.PortfolioConstruction`

```text
backend/src/FinanceSentry.Modules.PortfolioConstruction/
├── PortfolioConstructionModule.cs          IModuleRegistrar; Options binding; feature flag
├── Domain/
│   ├── Archetype.cs                        config record: gates, ordering, default constraints
│   ├── PortfolioConstructor.cs             pure: universe + archetype + constraints → proposal
│   ├── GapCalculator.cs                    pure: proposal + current book → gap lines
│   └── Ports/                              consumer-side ports, Integration adapters
├── Application/
│   ├── Queries/GetUniverseScreenQuery.cs
│   ├── Queries/ConstructPortfolioQuery.cs
│   ├── Queries/GetConstructionGapQuery.cs
├── Infrastructure/Persistence/             none in v1 (archetype lives on the IPS, spec OQ-7)
└── API/Controllers/PortfolioConstructionController.cs
```

Archetypes are bound from configuration (`PortfolioConstruction:Archetypes`), so a new archetype is a config
change (spec FR-012). v1 ships `growth` and `balanced` (spec OQ-2).

### Surfaces

| REST (`/api/v1/…`)                                | MCP tool               | Spec |
| ------------------------------------------------- | ---------------------- | ---- |
| `GET /portfolio-construction/universe`            | `get_universe_screen`  | US1  |
| `GET /research/scorecards/{symbol}`               | `get_scorecard`        | US2  |
| `GET /portfolio-construction/proposal?archetype=` | `construct_portfolio`  | US3  |
| `GET /portfolio-construction/gap?archetype=`      | `get_construction_gap` | US4  |

All endpoints are `[Authorize]`. The anonymous list in `ApiAuthenticationPipelineTests` is unchanged. The MCP
canonical list (`backend/tests/FinanceSentry.Mcp.Tests/ContractTests/ToolNameContractTests.cs`) and
`docs/mcp.md` are updated in the PR that adds the tools.

### Frontend

- `frontend/src/app/modules/portfolio-construction/`: universe screen, proposal page and gap view, each with a
  page-scoped signal store (the state, computed, methods, effects and store split).
- Scorecard strip: an "axis chip strip" pattern built in lifekit-common first (composition of `cmn-chip` /
  `cmn-tag` plus `cmn-disclosure-row`), then consumed here and offered to #824 for
  `frontend/src/app/modules/assets/pages/asset-dossier/`.
- Verdict words and labels come from a constants file. Amounts go through `MoneyUtils` / the `money` pipe.

## Delivery Shape (November)

Each step is one PR, behind the feature flag:

1. **Verdict calibration** (docs + test fixture): run the draft rule table (spec OQ-4) over stored
   `CandidateScore` rows, then fix the thresholds and rules version in the spec.
2. **Universe scorecards**: Research entities, migration, `UniverseScoringJob`, `VerdictRules`, the published
   reader, and the scorecard REST and MCP read.
3. **Constructor**: the new module, archetype config, `IProposedBookEvaluator`, and the proposal REST and MCP
   endpoints.
4. **Strip** in lifekit-common (Storybook-first), then the version bump here.
5. **Universe screen and proposal UI**, with #467 baselines at 390 px and desktop.
6. **Gap mode**: backend and UI.

The dossier header adoption of the strip is #824's own slice.

## Risks

- **Verdict is a trade call.** The owner chose `STRONG BUY / BUY / WAIT / AVOID` (spec OQ-3). Mitigated by the
  always-visible facts and fired rule, the versioned rule table, and the product never placing a trade (019 FR-014).
- **Upstream cost.** Valuation snapshots fetch live per symbol. The universe is bounded by the shortlist cap
  plus the curated list, and the job is rate-bounded like stage 1.
- **Thin universe** for the cold-start user if the shortlist fails. A configured curated list keeps the
  universe non-empty, and the empty state is explicit.
