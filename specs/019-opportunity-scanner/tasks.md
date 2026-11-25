# Tasks: Opportunity Scanner

**Feature**: `019-opportunity-scanner` | **Branch**: `019-opportunity-scanner`
**Input**: plan.md, spec.md, data-model.md, contracts/mcp-tools.md, research.md, quickstart.md
**Tests**: REQUIRED — pure scoring functions Test-First (SC-001).
**Scope**: v1 = US1 (conviction scoring) + US3 (promote/reject/expire). US2 machine-scan + `scan_opportunities` deferred to v2. FR-006b/c deferred to v1.1.

Paths under repo root. Feature lives in `FinanceSentry.Modules.Research` + 2 Core seams. **Build gate**: `dotnet build backend/` zero warnings after every `.cs` (via `mcr.microsoft.com/dotnet/sdk:9.0` container).

---

## Phase 1: Setup
- [X] T001 Confirm Research test project exists (`backend/tests/FinanceSentry.Modules.Research.Tests`) — reuse for Opportunity tests.

## Phase 2: Foundational — Core seams + domain + persistence (blocks all)
- [X] T002 [P] Core `backend/src/FinanceSentry.Core/Interfaces/IMarketStructureReader.cs` + `MarketStructureSnapshot` record (per data-model). Impl `MarketStructureReader` in `backend/src/FinanceSentry.Modules.Radar/Application/Services/MarketStructureReader.cs` over `IStructureQueryService`, projecting `TickerStructure` → snapshot; register in `RadarModule.cs`.
- [X] T003 [P] Core `backend/src/FinanceSentry.Core/Interfaces/IRiskPolicyGate.cs` + `RiskGateVerdict`/`RiskGateDecision`. Impl `RiskPolicyGate` in `backend/src/FinanceSentry.Modules.Risk/Application/Services/RiskPolicyGate.cs` over `CheckRiskRulesQueryHandler` (build `CheckRiskRulesQuery(userId, new RiskProposal(ticker, proposedUsd, overrideFlag))`, project `Verdict`; `HasRuleSet==false` → Allowed + "no rules on file"). Register in `RiskModule.cs`.
- [X] T004 [P] Research `Domain/Opportunity/` enums: `CandidateSource`, `CandidateStatus`, `CrowdingClass`.
- [X] T005 [P] Research entities `Domain/OpportunityCandidate.cs`, `Domain/CandidateScore.cs` (per data-model; no composite column).
- [X] T006 Repository interfaces `Domain/Repositories/ICandidateRepository.cs` (UpsertActiveAsync, FindActiveByTickerAsync, GetAsync, ListAsync(status?,source?), ListExpiredAsync) + `ICandidateScoreRepository.cs` (AppendAsync, LatestForCandidateAsync).
- [X] T007 `ResearchDbContext` (+ repo impls): add `DbSet<OpportunityCandidate>` + `DbSet<CandidateScore>`, entity config (indexes per data-model; jsonb `NominationReasons`/`IpsFit`/`Evidence` via HasConversion **with ValueComparer**; enums as string). Repo impls in `Infrastructure/Persistence/Repositories/`.
- [X] T008 Migration `M006_OpportunityCandidates` (`dotnet ef migrations add M006_OpportunityCandidates --project backend/src/FinanceSentry.Modules.Research --context ResearchDbContext`) — EF CreateTable (PascalCase; no raw SQL). Verify `database update` applies.

**Checkpoint**: seams + schema exist; build clean; migration applies (live boot later).

---

## Phase 3: User Story 1 — Conviction scoring (P1) 🎯 MVP

### Tests (write first)
- [X] T009 [P] [US1] `Opportunity/StructureScorerTests.cs`: RS/extension/z-score → 0–100 piecewise; not-evaluable window → null; deterministic.
- [X] T010 [P] [US1] `Opportunity/FundamentalsScorerTests.cs`: rev YoY + margin level/trend + EPS YoY → 0–100; missing EDGAR → null (partial, not faked); div-by-zero guarded.
- [X] T011 [P] [US1] `Opportunity/CrowdingClassifierTests.cs` + `ScoreCandidateHandlerTests.cs`: crowding Early/Normal/Extended by threshold; re-score appends a CandidateScore (no duplicate candidate, US1.4); held-ticker IPS concentration flag (US1.2).

### Implementation
- [X] T012 [P] [US1] `Domain/Scoring/FundamentalMath.cs` — extract concept-name mapping + margin/YoY helpers shared with `ThesisBreakEvaluator` (reuse, do not duplicate the concept table; refactor 017's evaluator to use it if low-risk, else share the `ThesisMetric` constants + one ratio source).
- [X] T013 [P] [US1] `Domain/Scoring/StructureScorer.cs` (pure): `MarketStructureSnapshot` → structure score + evidence.
- [X] T014 [P] [US1] `Domain/Scoring/FundamentalsScorer.cs` (pure): `IReadOnlyList<FundamentalFact>` → fundamentals score + evidence; not-evaluable path.
- [X] T015 [P] [US1] `Domain/Scoring/CrowdingClassifier.cs` (pure): extension + volume ratio → `CrowdingClass` (config thresholds).
- [X] T016 [US1] `Application/Commands/ScoreCandidateCommand.cs` + handler: resolve structure (`IMarketStructureReader`), fundamentals (`ISecEdgarService`), IPS (`IIpsRepository`) + current weight (`IBrokerageHoldingsReader`); run scorers; upsert active candidate (source User) or append re-score; emit `info` nomination signal (`IRadarSignalWriter`, Scanner `opportunity`); record `Created` candidate event on first create (`IThesisEventRecorder`); top-tier rule → `notable` signal + `GenerateOpportunityAlertAsync` (≤1/candidate/window). Register handler in `ResearchModule`.
- [X] T017 [US1] Config `OpportunityOptions` (crowding thresholds, top-tier bar, TTL days, trigger-prefill drawdown/buffer defaults, FormulaVersion) bound from configuration — no magic numbers.
- [X] T018 [US1] Alerts: add `AlertType.Opportunity` const; `GenerateOpportunityAlertAsync` on `IAlertGeneratorService` (Core) + impl in `AlertGeneratorService` (FindActive→HasRecent→AddAsync, deterministic referenceId per candidate).

**Checkpoint**: `score_candidate` produces an explainable scorecard; US1 independently testable.

---

## Phase 4: User Story 3 — Promote / reject / expire (P2)

### Tests (write first)
- [X] T019 [P] [US3] `Opportunity/TriggerPrefillTests.cs`: prefill formula (no price_drawdown trigger; revenue_yoy<0 if rev YoY positive; gross_margin< latest−buffer if evaluable) → valid `ThesisMetric`s; rounded/cited.
- [X] T020 [P] [US3] `Opportunity/PromoteRejectExpireHandlerTests.cs`: promote Refused by gate (fake `IRiskPolicyGate`) → no thesis, verdict returned; Allowed → SaveThesis called + candidate Promoted + Promoted event; override records signal; reject → Rejected + reason + event; expire past TTL → Expired + final score snapshot.

### Implementation
- [X] T021 [US3] `Domain/Scoring/TriggerPrefill.cs` (pure): scorecard → `List<ProposedTrigger>` → `ThesisInvalidationTrigger`s.
- [X] T022 [US3] `Application/Commands/PromoteCandidateCommand.cs` + handler: call `IRiskPolicyGate.CheckProposalAsync`; if `Refused` && !override → return verdict, no thesis; else build `SaveThesisCommand` (prefilled or caller triggers), execute, set `PromotedThesisId`/status Promoted, record `Promoted` event; override → emit override signal. Register handler.
- [X] T023 [US3] `Application/Commands/RejectCandidateCommand.cs` + handler: status Rejected + reason; record `Rejected` event. `Application/Commands/ExpireCandidatesCommand.cs` + handler: expire Active past `ExpiresAt`, final score snapshot + `Expired` event.
- [X] T024 [US3] `Application/Queries/ListCandidatesQuery.cs` + handler: candidates (filter status/source) + latest score.
- [X] T025 [US3] `Infrastructure/Jobs/CandidateExpiryJob.cs` (daily) → runs ExpireCandidatesCommand; register in `ResearchModule` JobRegistrar (`Cron.Daily`) + `AddScoped`.

**Checkpoint**: full lifecycle works; gate enforced.

---

## Phase 5: MCP surface (P1)

### Tests (write first)
- [X] T026 [P] Update `backend/tests/FinanceSentry.Mcp.Tests/ContractTests/ToolNameContractTests.cs` allowlist 43→47 (+score_candidate, list_candidates, promote_candidate, reject_candidate) + assertion message. Add parity registrations in `IntegrationTests/ToolParityTests.cs` for `IMarketStructureReader`, `IRiskPolicyGate`, candidate repos + scorers (+ a `FakeMarketStructureReader`/`FakeRiskPolicyGate` if live deps are unavailable in-test), and register the 4 tools.

### Implementation
- [X] T027 [P] `backend/src/FinanceSentry.Mcp/Tools/ScoreCandidateTool.cs` (`score_candidate`).
- [X] T028 [P] `ListCandidatesTool.cs` (`list_candidates`).
- [X] T029 [P] `PromoteCandidateTool.cs` (`promote_candidate`) — returns thesis id + gate verdict.
- [X] T030 [P] `RejectCandidateTool.cs` (`reject_candidate`).

**Checkpoint**: 4 tools invocable; allowlist/parity green.

---

## Phase 6: Polish
- [X] T031 [P] `dotnet build backend/` 0 warnings; `dotnet test` Research.Tests + Mcp.Tests green.
- [X] T032 Live boot: rebuild API, confirm M006 applies (research: opportunity_candidates + candidate_scores), API healthy, candidate-expiry job registers, no migration failure. (Same live-boot check that caught 017/018 bugs.)
- [X] T033 [P] Grep the scoring path for any LLM/messaging dependency — none (FR-002/FR-014). Confirm `scan_opportunities` (v2) and FR-006b/c (v1.1) are NOT silently half-built — deferrals are clean.

---

## Dependencies & order
- Setup + Foundational (Core seams T002/T003, entities, DbContext, migration) block all.
- US1 (T009–T018) is the MVP: scorers (pure, parallel) → ScoreCandidate handler → alerts. T012 FundamentalMath before T014.
- US3 (T019–T025) needs US1's candidate + the gate seam (T003).
- MCP (T026–T030) after the commands/queries exist. Polish last.

### Parallel
- Core seams T002 (Radar) + T003 (Risk) parallel. Scorers T013/T014/T015 parallel after T012. Tools T027–T030 parallel.

## MVP
**Setup + Foundational + US1 + MCP score/list** = conviction-amplification demo (SC-002). US3 adds promote/reject/expire + the 022 gate.

## Implementation notes
Sonnet-implement per recipe. New Core seams (`IMarketStructureReader`, `IRiskPolicyGate`) mirror the
`IRadarSignalWriter`/`IBrokenThesisReader` pattern. NO composite score. Not-evaluable is labeled, never
faked. Promotion always runs the risk gate. Live-boot verify M006 after implementation.
