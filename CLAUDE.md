# Finance Sentry — Claude Context

> **Source of truth split**: For architecture principles, testing requirements, code quality gates, and branching rules — the constitution at [`.specify/memory/constitution.md`](.specify/memory/constitution.md) is authoritative. This file covers **current state only** (what's built, what's running, what's next). When in doubt, constitution wins.

## Project Overview

Finance Sentry is a personal finance aggregation app built as an ASP.NET Core 10 modular monolith + Angular 21 SPA. It integrates with TrueLayer and Monobank for bank data and Binance and Interactive Brokers for investments, with AI-driven portfolio analytics on top.

Sole developer: Denys. Spec-driven development via the **speckit** toolchain (constitution → spec → plan → tasks → implement).

---

## Stack

| Layer | Technology |
|---|---|
| Backend | ASP.NET Core 10 (.NET 10, C# 14), EF Core 10, PostgreSQL 14, hand-rolled CQRS (`FinanceSentry.Core.Cqrs`), Hangfire, Serilog |
| Frontend | Angular 21.2, TypeScript strict, standalone components, NgRx SignalStore (`@ngrx/signals`), lazy-loaded modules |
| UI library | `@lifekit-hq/ui` + `@lifekit-hq/tokens` + `@lifekit-hq/core` — published from [lifekit-common](https://github.com/lifekit-hq/lifekit-common) (GitHub Packages; `NODE_AUTH_TOKEN` needed for installs). Components, `ToastService`, `ErrorMessageService`, `ThemeService` |
| Auth | Custom `JwtAuthenticationMiddleware` (backend) + `AuthStore` signal store + functional `authInterceptor` (frontend). Access token lives **in memory only** (store signal); refresh token is an httpOnly/Secure/SameSite=Strict cookie set by the backend. Silent refresh fires on app init. |
| Infra | Docker Compose (single file for full stack) |

---

## How to Run

Everything runs in Docker:

```bash
cd docker
docker compose -f docker-compose.dev.yml up -d --build
```

Startup order enforced by health checks: `postgres → api → frontend`

| Service | URL |
|---|---|
| Frontend (Angular) | http://localhost:4200 |
| Backend API | http://localhost:5001/api/v1 |
| Health check | http://localhost:5001/api/v1/health |
| Swagger | http://localhost:5001/swagger |
| Hangfire dashboard | http://localhost:5001/hangfire |
| PostgreSQL | localhost:5432 (user: finance_user / pw: finance_password / db: finance_sentry) |

For faster frontend iteration, run `ng serve` locally while keeping API + DB in Docker:

```bash
# Terminal 1
cd docker && docker compose -f docker-compose.dev.yml up -d postgres api

# Terminal 2
cd frontend && npm start
```

---

## Mandatory Rules (auto-loaded)

The gates below apply to every change — they are imported into context on every session:

@docs/claude/frontend-rules.md
@docs/claude/backend-rules.md

## Reference Docs (open when relevant)

Not auto-loaded — follow these links when the task touches them:

- [Money semantics](docs/money-semantics.md) — **source of truth for every money calculation** (balance meaning per provider, liability signs, flow windows, snapshot rules); any PR changing money math updates it in the same diff
- [App state & key files](docs/claude/app-state.md) — what's built/running per feature; update the relevant block when a feature lands
- [QA guide](docs/claude/qa.md) — test creds, golden-path scenarios, post-implementation e2e process
- [AI development pipeline](docs/claude/ai-pipeline.md) — Claude/Qwen roles (Qwen path currently disabled)
- [Speckit agent context](docs/claude/speckit-context.md) — machine-appended Active Technologies / Recent Changes (owned by `.specify/scripts/bash/update-agent-context.sh`; never hand-grow this file's sections in CLAUDE.md again)
- [Program roadmap & backlog](specs/ROADMAP.md) — destination, radar architecture, unimplemented specs

## QA — Test User

`test@gmail.com` / `Darkfly21` — has TrueLayer, Monobank, Binance and IBKR connections. Full scenarios: [QA guide](docs/claude/qa.md).

---

## Naming & Planning Conventions (adopted 2026-08-12)

| Thing | Convention | Example |
|---|---|---|
| Branch | `<type>/<issue#>-<slug>` — type is a conventional-commit type; create via `gh issue develop <n> -b` so the branch links to the issue | `feat/411-canonical-book-figures` |
| Commit / PR title | Conventional commits, scope = module or spec number (release-please parses these). **Squash rule**: a PR's title must carry the *highest-ranked* type among its commits (`feat` > `fix` > `perf`/`refactor` > `docs` > `chore`) — squash makes the title main's only commit message, so a multi-increment branch titled after its last commit misfiles the whole PR in the changelog and can skip the version bump (PR #550: a `feat` branch merged under a `docs` title). Retitle before merging if needed. | `feat(mcp): …`, `fix(040): …` |
| Issue title | Imperative sentence, **no priority prefix** — priority lives in the `P1`/`P2` label and the Project field | `Asset Dossier — per-holding page …` |
| Issue body | Traceability first (destination / source — why this exists), then acceptance criteria (**P1 issues only**; P2/P3 stay one-liners until promoted), then shape in PR count | see #411 |
| Issue metadata | Type (Feature/Bug/Task) + milestone + `P1`/`P2` label + Project "Finance Sentry" Priority/Size fields. Size only on P1 (S=1 PR, M=2–3, L=4+) — never size the fog | — |
| Issue labels | `P1` (firm: sized, acceptance criteria) / `P2` (named fog, unsized); `needs-refinement` = not ready to work; `devclaw-ready` = dispatchable to the autonomous instance; area labels (`frontend`, `backend`, …) | — |
| PR body | What + why, then a **Validation** section stating exactly what was run and its result (the PR template scaffolds this) | see `.github/PULL_REQUEST_TEMPLATE.md` |
| Milestone | `M<n> — <outcome>` — named for the outcome, never a date | `M1 — Ledger earns its keep` |
| Releases | release-please maintains the release PR (version bump for `version.txt` + `frontend/package.json` + API csproj + CHANGELOG); the Weekly Release workflow merges it Mondays 08:00 UTC (`workflow_dispatch` = release now). Both workflows act through the `lifekit-release-bot` GitHub App token (`RELEASE_APP_ID` var + `RELEASE_APP_PRIVATE_KEY` secret) — GITHUB_TOKEN pushes fire no workflows, so with it the release PR never gets its required checks and never merges (#623). The app-token merge is a real push: tag + VPS deploy follow through their normal triggers. Never hand-bump versions or tag ad-hoc. | — |

Backlog planning happens in dedicated sessions (plan-backlog skill); every issue must trace to a destination. Main is protected — all changes land via PR (squash), CI green first, including agent work.

### Gold-standard divergences

Audited against [REPO-STANDARD.md](https://github.com/lifekit-hq/.github/blob/main/REPO-STANDARD.md) (issue #470, 2026-08-29). Where this repo deliberately differs:

- **Root files beyond README/CLAUDE**: `AGENTS.md` (house-accepted agent pointer), `CHANGELOG.md` + `version.txt` (release-please-owned — `release-type: simple` versions `version.txt`), `devclaw.json` / `global.json` / dotfiles (tool configs). All load-bearing; none are session artifacts.
- **Deploy is continuous, not release-gated**: every merge to main deploys to the VPS (`deploy.yml`). The weekly release cadence governs versioning/CHANGELOG/tags, not shipping — there is no package publishing in this repo, so "publishing hangs off release-created" has nothing to attach to.
- **Pre-commit hook covers the frontend only** (`.husky/pre-commit` via `core.hooksPath`, wired by `frontend`'s `prepare` script): lint-staged + full lint + format check. Backend gates (build, tests, `EnforceCodeStyleInBuild`) run in CI only — a `dotnet` build/test cycle is too slow for a commit hook.
- **Coverage floors**: frontend ratchet floors live in `angular.json` (test → `ci` configuration) and gate CI; the backend 80% gate in `backend-ci.yml` is present but commented out (coverage is below the constitution's §II floor — re-enable when it ratchets up).
- **Backend has no format-only lint step**: no `dotnet format --verify-no-changes` in CI or pre-commit; style is enforced at build time via `EnforceCodeStyleInBuild` in `backend/Directory.Build.props` instead.

## Infrastructure catalog (ecosystem rule)

The ecosystem's infrastructure inventory lives in
[`lifekit-dashboard/backend/infra.json`](https://github.com/lifekit-hq/lifekit-dashboard/blob/main/backend/infra.json)
(rendered with live health probes on the dashboard's Infrastructure page).
**Any PR here that adds/removes/moves infrastructure — a container, cron,
workflow, external service, bot, or secret — updates that catalog in the same
change** (companion PR to lifekit-dashboard). `creds` entries name where a
secret lives, never its value.

## Collaboration Style

- Responses must be short and direct. No trailing summaries — Denys can read the diff.
- Lead with the action, skip preamble.
- One fix at a time. Diagnose before pivoting.
- Never change `Host=postgres` to `localhost` to work around Docker issues — fix Docker instead.
- Never modify connection strings or env config as workarounds — fix the root cause.
- Do not create markdown files at the repo root. Only `README.md` and `CLAUDE.md` belong there. Session artifacts, debug notes, and how-to docs do not get their own files — put relevant content in `README.md` or the appropriate `.specify/` artifact.

## Memory (vault)

Durable knowledge about this project lives in the vault, not in provider memory: `~/memory/projects/finance-sentry/` - `plan.md` (facts), `STATUS.md` (in-flight, only while parked), `log.md` (dated events). Read `plan.md` + `STATUS.md` when starting work here; verify live state live (`gh`, `docker ps`). Write durable decisions and gotchas back to those pages in the same session (contract: `~/memory/README.md`). Claude Code auto-memory is disabled by policy (`CLAUDE_CODE_DISABLE_AUTO_MEMORY=1`); Codex/other agents follow the same pointer via `AGENTS.md`.
