# Implementation Plan: Asset Dossier

**Branch**: `goal/fs-421-asset-dossier-2026-08-31` | **Date**: 2026-09-02 | **Spec**: spec.md

## Summary

Add `GET /api/v1/research/assets/{symbol}/dossier` (US1) and the `/assets/:symbol` Angular page
(US2) that compose existing per-ticker reads (thesis, valuation, analyst actions, news, earnings,
radar signals, and position/tax-lots) into a single coherent view — the full pre-answer gather
that Ledger already uses, now surfaced in the UI. US3 (Ledger's read, on-demand AI + cache) is a
separate subsequent PR.

## Technical Context

**Language/Version**: .NET 10 / C# 14 (backend), Angular 21.2 / TypeScript strict (frontend)

**Primary Dependencies**: ASP.NET Core, EF Core, MediatR-lite CQRS, NgRx SignalStore

**Storage**: PostgreSQL (existing schemas; no new tables in US1)

**Testing**: xUnit + WebApplicationFactory (backend contract tests), Vitest + Playwright (frontend)

**Target Platform**: Linux container via Docker Compose

**Constraints**: Zero `dotnet build` warnings; ESLint zero-error; `--no-verify` commit in sandbox
(NODE_AUTH_TOKEN absent; CI enforces full frontend gate)

## Constitution Check

- Modular monolith: cross-module data via port interfaces in Integration layer ✓
- Code quality: zero warnings gate ✓
- UI library rule: no raw components; `cmn-*` from `@lifekit-hq/ui` ✓
- State management: NgRx SignalStore; component is declarative ✓
- File organization: one concept per file ✓

## Project Structure

### Documentation

```text
specs/421-asset-dossier/
├── spec.md
├── plan.md        ← this file
└── tasks.md
```

### Backend (US1)

```text
backend/src/FinanceSentry.Modules.Research/
├── Domain/Ports/
│   ├── IHoldingTaxLotsReader.cs     NEW — port for brokerage tax lot detail
│   └── IAssetSignalReader.cs        NEW — port for per-ticker radar signals
├── API/Responses/
│   └── AssetDossierResult.cs        NEW — aggregate response + section DTOs
└── Application/Queries/
    └── GetAssetDossierQuery.cs      NEW — fan-out query handler
    └── AssetDossierController.cs    NEW — GET /research/assets/{symbol}/dossier

backend/src/FinanceSentry.Integration/
├── HoldingTaxLotsAdapter.cs         NEW — IHoldingTaxLotsReader impl (BrokerageSync)
├── AssetSignalAdapter.cs            NEW — IAssetSignalReader impl (Radar)
└── CrossModulePortRegistration.cs   MODIFIED — register two new adapters

backend/tests/FinanceSentry.Tests.Integration/
└── Research/AssetDossierContractTests.cs  NEW — shape + auth tests
```

### Frontend (US2 — next session)

```text
frontend/src/app/
├── shared/enums/app-route/app-route.enum.ts   MODIFIED — add AssetDossier route
└── modules/assets/
    ├── models/dossier/dossier.model.ts         NEW — mirror of backend DTOs
    ├── services/dossier.service.ts             NEW — HTTP service
    ├── store/
    │   ├── dossier.state.ts
    │   ├── dossier.computed.ts
    │   ├── dossier.methods.ts
    │   ├── dossier.effects.ts
    │   └── dossier.store.ts
    └── pages/asset-dossier/
        ├── asset-dossier.component.ts
        └── asset-dossier.component.html
```

## Slice-by-Slice Notes

### US1 — Aggregate Read Endpoint

Files touched: Research/Domain/Ports (2 new), Research/API/Responses (1 new),
Research/Application/Queries (1 new), Research/API/Controllers (1 new),
Integration (2 new adapters, 1 modified registration, 1 modified csproj),
Tests.Integration (1 new test class).

Key design decisions:
- `IBookFiguresService` (Core) gives the base position without a cross-module port — Research
  already depends on Core.
- `IHoldingTaxLotsReader` (new port) gives IBKR tax lot detail; Radar module needs no port for
  signals because `IAssetSignalReader` (new port) wraps `ListSignalsQuery`.
- All 7 sub-queries fan out via `Task.WhenAll`; each wrapped in try/catch so one failing source
  never 500s the whole response.
- Thesis lookup: filter `GetThesesQuery` result by ticker client-side (small list, user-scoped).
- Analyst actions: `GetAnalystActionsQuery` with Ticker filter, last 90 days, limit 20.
- Earnings: `GetEarningsCalendarQuery` with Tickers=[symbol], next 90 days, pick nearest.
- Signals: `IAssetSignalReader` returns last 30 days, limit 50; UI renders sparkline + latest.
- Integration csproj gains `BrokerageSync` reference (needed by `HoldingTaxLotsAdapter`).

### US2 — Dossier UI Page (next session)

Files touched: AppRoute enum, new `assets` feature module (models, service, store, page component),
app.routes.ts, holdings component (add click navigation).

Playwright spec: click holding → dossier renders; navigate back.

### US3 — Ledger's Read

Files touched: Research/Domain (`AssetLedgerRead` entity, `IAssetLedgerReadRepository`,
`ILedgerNarrator` port), Research/Application (`LedgerReadComposer`, `LedgerReadStaleness`,
`GetAssetLedgerReadQuery`, `GenerateAssetLedgerReadCommand`), Research/API
(`AssetLedgerReadResult`, two routes on `AssetDossierController`), Research/Infrastructure
(repository + `ResearchDbContext` + migration M014 + model snapshot), `FinanceSentry.API`
(`Adapters/LedgerNarratorAdapter`, one registration in `Program.cs`), plus the frontend `assets`
module (model/service/store/page section), the error registry, and the Playwright spec.

Key design decisions:

- **API shape**: `GET /research/assets/{symbol}/narrative` returns the cached read and never
  invokes the agent; `POST` (optional `?force=true`) generates. Two verbs on one route rather than
  a `/generate` sub-path — GET is the page-load path, POST is the mutation.
- **Agent access via a port**, not a project reference: `ILedgerNarrator` in Research's
  `Domain/Ports`, implemented by `LedgerNarratorAdapter`. The adapter lives in
  `FinanceSentry.API` rather than `FinanceSentry.Integration` (where the other 039-pattern
  adapters sit) because `Mcp → Integration` and `Agent → Mcp` already exist, so an
  `Integration → Agent` edge is circular. Program.cs already describes the host as the home for
  cross-module adapters.
- **Cache**: one row per (user, symbol) in `research.asset_ledger_reads`, overwritten in place.
  Storing history was rejected — nothing reads a superseded narrative.
- **Invalidation is two-part** (`LedgerReadStaleness`): older than 24h, or the
  `SourceFingerprint` no longer matches the current dossier. The fingerprint is a SHA-256 over the
  dossier's material facts only — it deliberately excludes `AssetDossierResult.GeneratedAt`, which
  moves on every request and would otherwise make every read instantly stale.
- **Staleness is computed on GET**, which means GET runs the dossier fan-out to recompute the
  fingerprint. That is the same fan-out the page's dossier request already performs and each
  branch is individually fault-tolerant; if it throws, the cached narrative is still served and
  staleness degrades to age-only. The alternative — folding the read into `AssetDossierResult` —
  was rejected because it churns the US1 contract.
- **A stale read is rendered, not hidden**, flagged with an "out of date" tag plus a Regenerate
  button. Blanking the section on staleness would trade a slightly-old answer for no answer.
- **No narrative → 503** `LEDGER_READ_UNAVAILABLE` (`ApiException` subclass, picked up by the
  existing `ErrorHandlingMiddleware`), so an unconfigured or failing agent is a retryable
  condition in the UI rather than a 500.
- No new UI component was needed — the section composes existing `cmn-card` / `cmn-button` /
  `cmn-alert` / `cmn-tag`, so the library-first rule is satisfied without a lifekit-common PR.

## Edge-case closure (increment 6) — frontend only

Surface: `assets/store/dossier.computed.ts`, `pages/asset-dossier/*`, their specs, and
`e2e/asset-dossier.spec.ts`. No backend change.

- **"No data" is a page state, not a per-section one.** Every section already hid itself when its
  source was empty, so a symbol with nothing on file rendered a bare header over whitespace — the
  spec's "unknown ticker → page shows a no-data state" edge case was unmet. `hasDossierSections`
  derives emptiness in the store and the page swaps to `cmn-empty-state` (library component, no
  lifekit-common PR needed). It mirrors the template's render conditions exactly, including
  treating a `notApplicable` valuation as absent — otherwise every crypto symbol would count as
  "has data" on the strength of a section the UI never draws.
- **The Ledger's-read card is hidden in the no-data state.** There are no facts to summarise, so
  offering to run the agent would spend a model call to be told "nothing on file".
- **`isStale` is a property of a narrative, not of the endpoint.** `GetAssetLedgerReadQuery`
  returns `IsStale: true` when no cache row exists (contract-tested), which the UI was rendering
  as an "out of date" tag next to a Generate button on every first visit. Fixed in
  `isLedgerReadStale` — it now requires a narrative — rather than by changing the backend
  contract, which no other consumer misreads.
