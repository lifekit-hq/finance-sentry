# Feature Specification: Asset Dossier

**Feature Branch**: `goal/fs-421-asset-dossier-2026-08-31`

**Created**: 2026-09-02

**Status**: Complete — US1, US2 and US3 landed; edge cases closed (increment 6)

**Input**: Issue #421 — Asset Dossier — per-holding page rendering everything Ledger sees

## Context

The backend already holds thesis state, signal history, analyst actions, valuation, news, fundamentals,
and per-ticker earnings data — all MCP-only today. The UI shows none of it on a per-holding basis.
This feature adds `/assets/:symbol` as the app's differentiator: click any holding → full dossier.

---

## User Scenarios & Testing

### User Story 1 — Aggregate Read Endpoint (Priority: P1)

A single backend endpoint aggregates all per-ticker dossier data so the UI can render the full
dossier in one round-trip. Every data section is backed by existing reads; no new pipelines.

**Why this priority**: Blocking prerequisite for all UI work. Without it, no dossier page can render.

**Independent Test**: `GET /api/v1/research/assets/AAPL/dossier` returns 200 with the full shape;
no holdings configured → `position` is null; no thesis → `thesis` is null; each section degrades
gracefully when its source has no data.

**Acceptance Scenarios**:

1. **Given** an authenticated user, **When** they GET `/research/assets/AAPL/dossier`,
   **Then** response is 200 with `symbol: "AAPL"` and all section keys present (null when no data).
2. **Given** the user holds AAPL in IBKR, **When** they fetch the dossier,
   **Then** `position` is non-null with quantity, currentValueUsd, and taxLots populated.
3. **Given** no IBKR holding for the symbol, **When** they fetch the dossier,
   **Then** `position` is null — not an error.
4. **Given** an unauthenticated request, **When** they GET the dossier endpoint,
   **Then** response is 401.
5. **Given** a crypto symbol (e.g. `BTCUSDT`), **When** they fetch the dossier,
   **Then** `valuation.notApplicable = true` and `analysts` is null — no broken sections.

---

### User Story 2 — Dossier UI Page (Priority: P1)

Route `/assets/:symbol` renders the full dossier for the given symbol. Reachable by clicking any
holding row in the Holdings page or any account row that has a linked symbol.

**Why this priority**: The core user-facing value of the feature — "be able to see what Ledger sees."

**Independent Test**: Navigate to `/assets/AAPL` → page renders all populated sections, empty
sections are hidden (not broken). Back navigation returns to Holdings.

**Acceptance Scenarios**:

1. **Given** the user is on the Holdings page, **When** they click a holding row,
   **Then** they navigate to `/assets/:symbol` and the dossier renders.
2. **Given** the dossier page renders, **When** there is position data,
   **Then** a Position section shows quantity, value, unrealized P&L, and tax-lot table.
3. **Given** a section has no data (e.g. no thesis), **When** the page renders,
   **Then** that section is hidden — the page is not broken.
4. **Given** asset type is crypto, **When** the page renders,
   **Then** Analysts and Thesis sections are hidden; Position and News/Signals still show.

---

### User Story 3 — Ledger's Read (Priority: P2)

A "Ledger's read" section on the dossier page is generated on demand via the 040 agent loop,
cached server-side (invalidated on data change or daily), and renders instantly from cache.

**Why this priority**: High-value differentiator but requires US1 + US2 first. Separate PR.

**Independent Test**: Click "Generate Ledger's read" → loading state → cached text renders.
Second click returns the cached version instantly without re-running the agent.

**Acceptance Scenarios**:

1. **Given** no cached read exists, **When** user triggers "Generate", **Then** the 040 agent
   runs with all dossier data as context and the result is persisted and rendered.
2. **Given** a cached read exists, **When** user views the dossier, **Then** the cached text
   renders immediately without triggering the agent.
3. **Given** underlying holding data changes, **When** user views the dossier,
   **Then** the cache is stale and a "regenerate" prompt is shown.

---

### Edge Cases

- Symbol not in any holding and not in watchlist → dossier still loads, position section hidden.
- Symbol with no analyst coverage → analysts section hidden (null), not an error.
- Radar has no signals for this ticker → signals section shows empty list.
- Valuation query returns not-applicable for crypto → valuation section hidden in UI.
- Unknown ticker (typo in URL) → all data sections return empty/null; page shows "no data" state.
- Brokerage holding with no cost basis → P&L fields are null, not zero.

## Requirements

### Functional Requirements

- **FR-001**: `GET /api/v1/research/assets/{symbol}/dossier` MUST return 200 with the full
  `AssetDossierResult` shape for any authenticated user.
- **FR-002**: Dossier endpoint MUST read all sub-queries one after another: the handlers share the request's
  scoped `ResearchDbContext`, which allows one operation at a time, so a `Task.WhenAll` fan-out silently dropped sections.
- **FR-003**: Each dossier section MUST degrade gracefully: null/empty when source has no data,
  never a 500.
- **FR-004**: Position section MUST include tax lot detail for brokerage holdings (IBKR); for
  crypto, taxLots is empty.
- **FR-005**: Radar signals are filtered to the queried symbol (subject filter on ListSignalsQuery).
- **FR-006**: Analyst actions include the recommendation trend (6 months) when the ticker is in
  the analyst universe.
- **FR-007**: Next earnings event is the nearest future event for the symbol only.
- **FR-008**: Cross-module data (tax lots from BrokerageSync, signals from Radar) MUST be
  accessed via port interfaces in the Research module — no direct assembly coupling.
- **FR-009**: The dossier endpoint requires authentication (401 when no valid token).

### Key Entities

- **AssetDossierResult**: Aggregate response — symbol, position, thesis, valuation, analysts,
  recentNews, nextEarnings, radarSignals, generatedAt.
- **DossierPositionSection**: provider, quantity, currentValueUsd, costBasisUsd, unrealizedPnlUsd,
  unrealizedPnlPercent, taxLots[].
- **DossierTaxLotEntry**: quantity, currentValueUsd, averageCostUsd, costBasisUsd,
  unrealizedPnlUsd, unrealizedPnlPercent, acquiredAt, isLongTerm.
- **DossierAnalystsSection**: recentActions[], trends[], coverage string.
- **DossierSignalItem**: timestamp, scanner, signalType, severity, payload.
- **IHoldingTaxLotsReader**: Port in Research; BrokerageSync implementation in Integration.
- **IAssetSignalReader**: Port in Research; Radar implementation in Integration.

## Success Criteria

### Measurable Outcomes

- **SC-001**: Contract test for `GET /research/assets/{symbol}/dossier` passes green in CI.
- **SC-002**: All dossier sections degrade to null/empty — no 500 — when source has no data.
- **SC-003**: Backend build produces zero warnings after all new files are added.
- **SC-004**: Holdings page → click holding → `/assets/:symbol` renders all populated sections (US2).
- **SC-005**: Crypto symbol dossier hides Analysts + Thesis sections, shows Position + News (US2).

## Assumptions

- All backend data sources are already populated by existing ingestion jobs — no new pipelines.
- Tax lot data for crypto is out of v1 scope (Binance does not expose lot-level history).
- The "Ledger's read" AI section (US3) is a separate PR.
- The library rule applies: any new reusable UI primitive lands in lifekit-common first.
- Position lookup uses `IBookFiguresService` (Core) for the base position, plus `IHoldingTaxLotsReader`
  (port) for tax lot detail from BrokerageSync.
