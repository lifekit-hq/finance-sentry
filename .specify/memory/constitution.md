<!-- SYNC IMPACT REPORT - Constitution 1.4.0
Version: 1.3.1 → 1.4.0
Bump Type: MINOR (new principle; no existing principle changed)
Principles Added:
  - VII. Ledger Boundary — the Ledger agent's relationship to Finance Sentry is
    fixed as three clauses: MCP is Ledger's only tool surface; the hook payload
    carries ids only; no endpoint may name Ledger. Ruled by Denys 2026-09-17 with
    the Ledger heartbeat design calls; landed ahead of the wake dispatcher auth
    work, the detectors and the Events module that depend on it.
Sections Modified:
  - Code Review & Compliance: added an automatic-block trigger for changes that
    breach Principle VII.
Follow-up TODOs:
  - `research/assets/{symbol}/ledger-read` (GET/POST, AssetDossierController)
    predates this principle and names Ledger in an endpoint; rename tracked as a
    separate task.
  - Sweep remaining bank-sync components (connect-account, transaction-list,
    sync-status) to ConnectStore / TransactionsStore per Principle VI.
  - Repair or remove stale Playwright integration tests under
    frontend/tests/integration/bank-sync/.
Prior report (1.3.1, 1.3.0, 1.2.1 → 1.1.1 → 1.1.0) retained in git history.
-->

# Finance Sentry Constitution

## Core Principles

### I. Modular Monolith Architecture

All backend services MUST be organized as a modular monolith using ASP.NET 9+ with
domain-driven design. Each financial integration (banks, brokers, crypto) is a distinct,
self-contained module with clear contracts. Modules communicate through well-defined
service boundaries, never coupling directly to external APIs.

All external service integrations (bank APIs, broker APIs, crypto exchanges) MUST be
accessed via a domain-defined interface (e.g., `IBankProvider`, `IBrokerAdapter`).
No module may reference a concrete external adapter directly — concrete implementations
are registered in Infrastructure and resolved via dependency injection. This ensures
each integration is independently swappable, mockable in tests, and replaceable without
changing module business logic.

Modules are independently deployable and testable.

### II. Code Quality Enforcement (NON-NEGOTIABLE)

Strict code quality is non-negotiable. Backend: ESLint + Prettier (absolute enforcement),
StyleCop + code analyzers, zero-warning builds. Frontend: Angular linting (strict mode),
tsconfig strict: true, CSS linting. All commits fail pre-commit hooks if standards
violated. No exceptions in pull requests—violations block merge until resolved.

**Frontend ESLint — mandatory before any code is committed:**
Every Angular TypeScript file written or modified by the agent MUST pass `npx eslint <path>` with zero errors before the task is marked complete. Key rules enforced by `eslint.config.mjs`:
- `@angular-eslint/prefer-inject` — use `inject()` function, never constructor parameter injection
- `@angular-eslint/prefer-on-push-component-change-detection` — all components must set `ChangeDetectionStrategy.OnPush`
- `@angular-eslint/component-selector` — selector prefix is `fns`, style is `kebab-case`
- `@typescript-eslint/explicit-member-accessibility` — all class members need an explicit access modifier (`public`, `private`, `protected`); constructors use `no-public`
- `@typescript-eslint/no-magic-numbers` — extract numeric literals to named constants (exceptions: 0, 1, -1, array indexes, readonly class properties)
- `@typescript-eslint/naming-convention` — class properties: `camelCase` (no underscore prefix); static properties: `UPPER_CASE`; module-level variables: `UPPER_CASE` or `camelCase`
- `@typescript-eslint/no-use-before-define` — declare module-level functions before they are referenced
- `@typescript-eslint/no-unsafe-enum-comparison` — cast the non-enum side to the enum type before comparing
- `@typescript-eslint/no-unsafe-member-access` — type-narrow or cast `unknown`/`any` before accessing properties
- `simple-import-sort/imports` — run `eslint --fix` after writing imports to auto-sort; or order: third-party → `@angular/*` → project absolute → project relative
- `prettier/prettier` — run `eslint --fix` to auto-apply formatting; do not hand-format multi-line ternaries

**Workflow**: after writing each TypeScript file, run `npx eslint <file>` and fix all errors before proceeding.

**Backend C# Quality — mandatory build gate:**
Every C# file written or modified MUST produce zero `dotnet build` warnings before the
task is marked complete. Key analyzer codes and their required fixes:
- `IDE0005` — unused `using` directive → remove
- `IDE0290` — primary constructor available → apply
- `IDE0161` — block-scoped namespace → convert to file-scoped `namespace X;`
- `CS8618`/`CS8600`–`CS8604` — nullable reference warnings → resolve; never suppress
  with `!` without an explanatory comment
- `IDE0028`, `IDE0059`, `CS0168`, `CS0219` — unnecessary code → remove

Warnings-as-errors is the CI target state. Any PR that introduces new `dotnet build`
warnings is blocked. Use the `csharp-quality` skill for batch cleanup.

**Workflow**: after writing each C# file, run `dotnet build backend/` and fix all
warnings before proceeding.

### III. Multi-Source Financial Integration

The system MUST reliably aggregate data from multiple financial sources: bank APIs,
Interactive Brokers, Binance, and others. Each integration module MUST have isolated
data synchronization, error handling, and retry logic. Data consistency across sources
is verified through reconciliation tasks. APIs are treated as potentially unreliable—
failures are graceful.

### IV. AI-Driven Analytics & Insights

AI analysis is the core value proposition. Portfolio analysis, risk assessment, and
forecasts must be AI-backed. LLM integration (via documented API patterns) generates
asset reports and portfolio-level insights. Analytical results are cached and versioned.
Both individual asset and portfolio-level analytics require AI summarization and
recommendations.

### V. Security-First Financial Data Handling

All financial data is encrypted at rest and in transit. Authentication is enforced at
API boundary and per-module. User data isolation is absolute—queries, caching, and
reports must be user-scoped. Secrets are never logged. Audit logs record all data
access. No shortcuts on security—violations require explicit team lead approval.

**Token storage (non-negotiable):** The access token MUST live only in application
memory (the `AuthStore` signal). It MUST NOT be written to `localStorage`,
`sessionStorage`, or any other JS-readable persistence. The refresh token MUST be
delivered as an httpOnly, Secure, SameSite=Strict cookie set by the backend and
never read by the frontend. App startup silently calls `/auth/refresh` to
rehydrate the in-memory access token from the cookie. This is the minimum bar for
XSS resistance on a financial app — any regression (e.g. reintroducing a
`TOKEN_KEY` in `localStorage`) blocks merge.

### VI. Frontend State & Composition Discipline (NON-NEGOTIABLE)

Frontend architecture enforces a strict separation between UI and business logic.
Components are declarative; state, side effects, and cross-cutting resolution live
in dedicated layers. Violations block PR merge.

**1. State lives in NgRx SignalStore, not components.**
Any non-trivial feature state MUST be held in a `signalStore()` under
`frontend/src/app/modules/<feature>/store/`. The store MUST be split across five files:

- `<name>.state.ts` — state interface, literal unions, initial state
- `<name>.computed.ts` — derivations (`isLoading`, `errorMessage`, selectors)
- `<name>.methods.ts` — synchronous `patchState` mutations only
- `<name>.effects.ts` — `rxMethod`s for HTTP/async and hooks for router/signal effects
- `<name>.store.ts` — `signalStore(...)` composition

Components MUST NOT hold `isLoading`/`errorMessage`/fetched-data fields, MUST NOT
subscribe to observables, MUST NOT use `setInterval` (use `timer(...)` in an
`rxMethod`), and MUST NOT parse query params or run `ngOnInit` side effects.
Component responsibilities are limited to: form definitions, template bindings to
store signals, and one-line dispatch handlers. App-scope stores use
`{providedIn: 'root'}`; page-scope stores are provided at the component level.

**2. Custom providers are extracted as `provide*()` factories.**
Any provider beyond Angular's built-in `provideX()` helpers (custom `ErrorHandler`,
custom injection tokens, `APP_INITIALIZER`, class-based `HTTP_INTERCEPTORS`, etc.)
MUST be extracted to `frontend/src/app/core/providers/<name>.provider.ts` as a
function returning `EnvironmentProviders` via `makeEnvironmentProviders(...)`. One
concern per file. `app.config.ts` lists `provide*()` calls only — no inline entries.
Feature-scoped providers live under `modules/<feature>/providers/`.

**3. Error-code → user-message resolution is centralized.**
The library `@dsdevq-common/ui` owns the mechanism (`ERROR_MESSAGES` injection
token + `ErrorMessageService.resolve(code)`). The app owns the data
(`src/app/core/errors/error-messages.registry.ts`). Components and stores MUST
NOT contain `if/else` ladders mapping `errorCode` to messages. A new backend
`errorCode` MUST be added to the registry in the same PR that introduces it.
Store computeds delegate to `ErrorMessageService.resolve(code)` and fall back to
a single feature-specific default string when the resolver returns `null`.

**4. UI library discipline (existing, reinforced).**
Any UI primitive used by the app MUST come from `@dsdevq-common/ui`. Raw `<input>`,
`<button>`, `<div class="error">` are forbidden when a `cmn-*` equivalent exists.
New primitives are added to the library first, never directly to `frontend/`.

**5. File organization: one concept per file; cross-module code in `shared/`.**
In Angular modules, each concept type has exactly one canonical home — no exceptions:
- Interfaces / types → `models/*.model.ts` or `models/*.types.ts`
- Constants → `*.constants.ts` adjacent to the consumer
- Component class → `*.component.ts` — no inline interface or constant definitions
- Service class → `*.service.ts` — no inline interfaces; import from model files
- Validators → `<feature>/validators/*.validator.ts`

Anything consumed by more than one feature module MUST live in
`frontend/src/app/shared/`. Placing cross-module types, utilities, or enums inside a
feature folder is a violation. Duplicating helpers across feature modules is a violation.
Mixing an interface definition into a component or service file is a violation.
All three block PR merge. Use the `frontend-code-quality` skill for audit sweeps.

### VII. Ledger Boundary (NON-NEGOTIABLE)

Finance Sentry owns the data and the events; the Ledger agent reads them. The
dependency is one-way and the boundary is fixed by three clauses:

1. **MCP is Ledger's only tool surface.** Every read Ledger performs against Finance
   Sentry goes through the `FinanceSentry.Mcp` tool surface. No REST endpoint, direct
   database access, or other channel is offered to Ledger as a tool.
2. **The hook payload carries ids only.** The event push from Finance Sentry to Ledger
   (the OpenClaw hook) contains identifiers — event id, kind, user id, entity ids — and
   nothing else. Ledger resolves what an id means by reading back through MCP; no
   amounts, names, narrative or other domain content ride on the hook.
3. **No endpoint may name Ledger.** No API route, MCP tool, job or payload field is
   named after Ledger or any other consumer. Finance Sentry surfaces are named for the
   domain concept they expose, so any reader can consume them and Ledger stays
   replaceable.

## Tech Stack Minimums

**Backend**: .NET Core 9+, ASP.NET with OpenAPI/Swagger documentation
**Frontend**: Angular 21.2+, TypeScript with strict mode, standalone components,
NgRx SignalStore (`@ngrx/signals`) for state, RxJS for async primitives,
`@dsdevq-common/ui` as the sole UI primitive library
**Database**: PostgreSQL 14+
**Message Queue/Async**: RabbitMQ or built-in hosted service (if monolith only)
**Containerization**: Docker for all services; Docker Compose for local development
**AI/LLM**: OpenAI API or compatible; documented prompts and request patterns
**Testing**: xUnit/.NET test framework for backend, Vitest + Playwright for frontend
**Monitoring**: ELK (Elasticsearch, Logstash, Kibana) or Application Insights for
structured logging

*Non-negotiable versions*: .NET 9+, Angular 20+, PostgreSQL 14+. Downgrades require
team lead approval.

## Development Workflow & Quality Gates

### Branching Strategy

All work MUST follow per-task feature branching discipline:

- **Branch Naming**: `<feature-id>-<description>` (e.g., `001-bank-account-sync`,
  `T211-bank-account-tests`)
- **Isolation**: Each task/story gets a dedicated branch from the current `main` or
  feature branch parent
- **Per-Task Branching**: Do NOT commit multiple independent tasks to a single branch.
  Each logical task/story must have its own branch for independent review and potential
  rollback
- **Lifecycle**:
  1. Create branch from `main` (or current feature parent if sub-task)
  2. Implement task, pass all tests, update versions if required (see Versioning &
     Tagging Policy)
  3. Open PR to `main` (or parent feature branch)
  4. Pass CI/CD (linting, tests, coverage)
  5. Code review approval (MUST verify compliance with Principles I–VII)
  6. Merge to `main`
  7. **Delete branch immediately after merge** (enforce via GitHub setting:
     "Automatically delete head branches")
  8. **Create new branch from updated `main`** if continuing work on next task

### Code Review & Compliance

Every PR MUST verify compliance with Core Principles I–VII. Violations block merge:

- Failing linter checks → automatic block
- Missing or incomplete tests → automatic block
- Non-encrypted data handling → automatic block
- Coupling between modules → automatic block
- External integration accessed without a domain interface (Principle I) → automatic block
- Frontend feature state held in a component, `setInterval` polling, inline error-code
  ladders, or custom providers not extracted under `core/providers/` (Principle VI) →
  automatic block
- Interface or constant defined inline inside a component or service file (Principle VI.5) →
  automatic block
- Cross-module type, utility, or enum not in `shared/` (Principle VI.5) → automatic block
- Backend `dotnet build` warnings not resolved (Principle II) → automatic block
- Ledger given a tool surface other than MCP, a hook payload carrying more than ids, or
  an endpoint/tool/field named after Ledger (Principle VII) → automatic block
- Version NOT bumped on frontend/API changes → automatic block (see Versioning & Tagging
  Policy)
- Tag NOT created for version bump → automatic block (see Versioning & Tagging Policy)

Code review checklist includes security, testability, adherence to modular boundaries,
and version/tagging compliance.

### Testing Discipline

- **Unit Tests**: Required for all business logic (>80% coverage target)
- **Integration Tests**: Required for inter-module contracts and API boundaries
- **Contract Tests**: Two mandatory categories:
  1. **External API contracts**: Required for every integration with an external service
     (banks via Plaid, Interactive Brokers, Binance, etc.). Tests validate that the
     external API still conforms to the shape the domain interface expects.
  2. **REST endpoint contracts**: Required for every REST endpoint exposed by the
     backend. Tests validate request/response schema and status codes independently
     of business logic. Each new endpoint MUST ship with a corresponding contract test
     in the same PR.
- **Test-First**: Tests written and passing BEFORE feature implementation (TDD)
- All tests must run in CI/CD pipeline; red pipeline blocks merge

### Versioning & Tagging Policy

#### Frontend (Angular SPA) Versioning

- **Location**: `frontend/package.json` - `"version"` field
- **Trigger**: Any change to Angular components, services, models, styling, or routing
- **Semver Scheme**: MAJOR.MINOR.PATCH
  - **MAJOR**: Breaking UI changes, major API contract change (requires backend
    coordination)
  - **MINOR**: New component/feature, new service method, non-breaking changes
  - **PATCH**: Bug fixes, dependency updates, style refinements
- **Requirement**: Version bump MUST be committed in the same PR as the feature/fix
- **CI/CD Check**: Pipeline validates that version in `package.json` matches the commit
  (MUST increment if any .ts/.html/.scss changed under `frontend/src/`)

#### API (Backend) Versioning

- **Location**: `FinanceSentry.API.csproj` - `<PropertyGroup><Version>` field, and
  OpenAPI/Swagger documentation
- **Trigger**: Any change to REST API contract (new endpoint, parameter addition,
  response schema change, deprecation)
- **Semver Scheme**: MAJOR.MINOR.PATCH
  - **MAJOR**: Breaking endpoint changes (e.g., parameter removal, response structure
    incompatible with clients)
  - **MINOR**: New endpoints, new optional parameters, new response fields
  - **PATCH**: Bug fixes, security updates, endpoint improvements (no client impact)
- **Requirement**: Version bump MUST be committed in the same PR as the API change
- **CI/CD Check**: Pipeline validates version increment on PR
- **OpenAPI/Swagger Update**: API documentation in `docs/SWAGGER.md` (or
  auto-generated) MUST reflect version

#### GitHub Tags

- **Naming Convention**: `v<MAJOR>.<MINOR>.<PATCH>` (e.g., `v0.2.0`, `v1.0.0-beta`)
- **Scope**: Tags MUST be created for **both** frontend and backend version bumps
  - If only frontend changes: tag as `frontend-v<VERSION>` (e.g., `frontend-v0.2.0`)
  - If only backend changes: tag as `backend-v<VERSION>` (e.g., `backend-v0.1.0`)
  - If both change in same PR: **coordination required**—create separate tags or single
    tag if coordinated release
- **Timing**: Tag MUST be created after merge to `main` (via GitHub release automation
  or manual `git tag` + push)
- **Release Notes**: Each tag MUST have release notes documenting changes (generated
  from PR description + linked issues)
- **Automation**: CI/CD SHOULD automatically create tag on merge if version detected
  (recommended: GitHub Actions workflow `on: push to main`)

#### Combined Example Workflow

```
1. Developer creates branch: git checkout -b T206-plaid-link-endpoint
2. Implements POST /accounts/link REST endpoint
3. Adds contract test for POST /accounts/link in same PR (mandatory per Testing
   Discipline)
4. Bumps backend version in FinanceSentry.API.csproj: 0.1.0 → 0.1.1
5. Commits: "feat: implement POST /accounts/link endpoint (closes T206)"
6. Pushes branch, opens PR
7. CI/CD:
   - ✅ Builds backend
   - ✅ Runs tests (coverage >80%)
   - ✅ Linting passes
   - ✅ Contract test for POST /accounts/link passes
   - ✅ Validates version bump: 0.1.0 → 0.1.1 detected in .csproj ✓
8. Code review approves
9. Merge to `main`
10. GitHub Actions detects version bump → creates tag `backend-v0.1.1`
11. Release notes auto-populated from PR
12. Branch deleted (automatic)
13. Developer creates new branch for next task: git checkout -b T207-get-accounts
```

### Deployment Process

1. Feature branch → Pull Request (CI: build + lint + tests + version/tag validation)
2. Code review approval (MUST verify compliance + version correctness)
3. Merge to `main`
4. **Automatic tag creation** (if version detected) or manual tag if required
5. Automated deployment to staging (via Docker)
6. Manual verification in staging
7. Production deployment (manual gate—team lead approval)

Rollback procedure documented and tested monthly. **Rollback includes tag management**:
if reverting commits, delete associated version tags and recreate if necessary.

## Governance

The constitution supersedes all other development practices. Amendments require
documentation of rationale, impact on current tasks, and approval by team lead (Denys).

**Compliance Enforcement**: All PRs are subject to automated compliance checks (linting,
tests, security, version bumps, tagging) and manual verification per architecture.
Violations require explicit remediation or team lead approval.

**Principles Trump Convenience**: When speed conflicts with a principle, the principle
wins. Exceptions are documented and tracked.

**Branch Discipline**: Per-task branching is MANDATORY. Mixed multi-task branches
require explicit team lead approval (rare exception for coordinated refactors).

**Version & Tagging Enforcement**: Automatic CI/CD checks MUST validate version bumps
and tag creation. Missing version bump or tag blocks PR merge.

**Version Policy**:
- MAJOR bump for principle additions/removals or backward-incompatible API changes
- MINOR bump for new principles/sections or new API endpoints
- PATCH bump for clarifications, typos, or API bug fixes
- Each version change increments **Last Amended** date (ISO format)
- Applies to both constitution versioning and feature versioning (frontend/backend)

**Version**: 1.4.0 | **Ratified**: 2026-03-21 | **Last Amended**: 2026-09-17
