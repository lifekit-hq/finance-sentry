# Finance Sentry — Agent Harness

ASP.NET Core 10 modular monolith + Angular 21 SPA aggregating bank, crypto and brokerage data.
Deeper context: [`CLAUDE.md`](CLAUDE.md) (conventions, rules), [`specs/`](specs) (per-feature
spec → plan → tasks), [`docs/`](docs) (money semantics, MCP catalogue, QA).

## Commands

```bash
# Backend — restore first; --no-restore fails on a fresh clone
cd backend
dotnet restore FinanceSentry.sln
dotnet build FinanceSentry.sln --no-restore -c Release
dotnet test FinanceSentry.sln --no-build -c Release
```

```bash
# Frontend — requires NODE_AUTH_TOKEN (a GitHub token with read:packages) for @lifekit-hq/*
cd frontend
npm ci
npm run lint             # ng lint --max-warnings 0; it does not auto-fix
npm run format:check     # a CI gate too — `npm run format` writes the fixes
npm run build
npm run test:ci          # Vitest unit tests, in a real Chromium (npx playwright install chromium)
npx playwright test      # e2e; the config writes playwright-report/results.json
```

Playwright is **required** when the change touches app-surface UI (`src/app/**`, `angular.json`):
unit tests never render the integrated app.

Run the full stack with `cd docker && docker compose -f docker-compose.dev.yml up -d --build`
(frontend :4200, API :5001).

**Pre-commit hook** (`.husky/pre-commit`, wired by `frontend`'s `prepare` script): when frontend
source files are staged it runs lint-staged, `npm run lint` and `npm run format:check` —
the same checks as the Frontend CI job, seconds once `frontend/node_modules` exists. It does *not*
run `npm ci` and does *not* enforce a version bump (release-please owns versions, deriving them
from Conventional Commit messages). Commit normally; a failure with `eslint … ENOENT` means
`frontend/node_modules` is missing — the fix is `cd frontend && npm ci`, not skipping the hook.

Memory-constrained machine? `dotnet test -m:1` and `NODE_OPTIONS=--max-old-space-size=2048` keep
the runs inside ~4 GB; the defaults parallelise until the OOM killer intervenes.

## Layout

```
backend/src/     FinanceSentry.API (entry point, DI, middleware) · .Core (CQRS interfaces, shared
                 primitives) · .Infrastructure (encryption, cross-cutting) · .Integration
                 (adapters wiring one module's ports to another's data — the cross-module seam) ·
                 .Mcp (MCP server) · .Gateway · .Modules.* (one project per bounded context;
                 `ls backend/src` is the list)
backend/tests/   FinanceSentry.Tests.Unit (pure, no DB) · .Tests.Integration (WebApplicationFactory
                 + mocked external I/O) · .Mcp.Tests (tool contract + schema) · .Modules.*.Tests
                 for the modules that have their own suite — the older modules are covered from
                 Tests.Unit / Tests.Integration instead
frontend/        src/ is the Angular app; e2e/ is its sibling and holds the Playwright specs
                 (e2e/live/ runs against a live stack via playwright.live.config.ts)
```

## Test strategy

- Integration tests mock external I/O (repos via Moq, DB contexts via `UseInMemoryDatabase`).
  DB-heavy tests carry `[Trait("Category","Integration")]`; container-dependent ones use
  `[DockerRequiredFact]` (`backend/tests/FinanceSentry.Tests.Integration/Shared/`), which skips at
  discovery time when no Docker daemon is reachable. Keep both conventions.
- CI (`backend-ci.yml`) runs the solution **unfiltered** against a `postgres:14-alpine` service, so
  `--filter "Category!=Integration"` is a local convenience, never a way to keep a test out of CI.
- MCP tools: `docs/mcp.md` is the catalogue; the canonical list is asserted by
  `backend/tests/FinanceSentry.Mcp.Tests/ContractTests/ToolNameContractTests.cs`.

## Key patterns

- **CQRS (MediatR-lite)**: `ICommand<TResult>` → `ICommandHandler<,>`, `IQuery<TResult>` →
  `IQueryHandler<,>`, all under each module's `Application/Commands/` or `Application/Queries/`.
- **HTTP response shapes**: contract tests declare a local `*Shape` record and assert on it. A field
  added to a shape must also exist on the Application-layer `*Result` — a mismatch deserialises as
  `null` instead of failing to compile.
- **Auth**: `JwtAuthenticationMiddleware` reads the `fs_access_token` cookie; tests inject it with
  `client.DefaultRequestHeaders.Add("Cookie", $"fs_access_token={jwt}")`.
- **Encryption**: handlers storing API keys take `ICredentialEncryptionService`; test harnesses wire
  `Encryption:CurrentKeyVersion`, `Encryption:Keys:1` and `Deduplication:MasterKeyBase64`.
- **Angular templates**: `@angular-eslint/template/attributes-order` orders attributes bound
  properties → plain attributes → event bindings → structural slot markers. `npm run lint` only
  reports it; lint-staged (`eslint --fix`) is what rewrites staged files.
- Each in-memory DB gets a GUID name per `WebApplicationFactory` subclass — never share one.
  Factory mocks use `MockBehavior.Loose`: set up only the calls the test path makes.
