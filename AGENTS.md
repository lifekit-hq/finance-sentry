# Finance Sentry — Agent Harness

ASP.NET Core 10 modular monolith + Angular 22 SPA. Spec-driven via speckit. This file is a table of contents:
commands, where truth lives, and the gotchas that cost a session. Everything else is in the linked docs.

## Quick-start

```bash
# Restore + build (required on a fresh clone or after switching branches)
cd backend && dotnet restore FinanceSentry.sln
dotnet build FinanceSentry.sln --no-restore -c Release

# Run tests — no filter, no -m flags (`backend/Directory.Build.rsp` pins one build node).
# `scripts/test-backend.sh` runs the same in a 2 GB SDK container (docker/docker-compose.test.yml),
# the production host's per-build limit. CI runs the full solution too; container-backed tests
# report themselves as Skipped where no Docker daemon is reachable.
dotnet test FinanceSentry.sln --no-build -c Release
```

### Claude Code hooks (`.claude/hooks/`, plain Python, no model call)

`tests/harness/run.sh` proves each hook in both directions (blocked + allowed case); it runs in CI inside `frontend-checks` (`lint-build-test`). PreToolUse blocks `Edit`/`Write` on `Migrations/`, lint/format/style configs and screen baselines (exit 2; open an issue or state the reason in the plan); PostToolUse runs `eslint --fix` / `dotnet format` on the touched file. Decisions log to gitignored `.claude/hooks.log.jsonl`.

### Frontend (lint, build, e2e — required when touching app-surface UI)

```bash
# @lifekit-hq/* installs from GitHub Packages: export NODE_AUTH_TOKEN=$(gh auth token)
# (token needs read:packages). See frontend/.npmrc.
cd frontend
npm ci
npm run lint
npm run build
npx playwright test --reporter=json
```

Commit hook (`.husky/pre-commit`, lint-staged on staged frontend files) assumes `frontend/node_modules` exists and is not to be bypassed. Full hook and CI load-gate detail: [app-state](docs/claude/app-state.md).

> **Source of truth split**: architecture principles, testing requirements, code quality gates and branching rules live in the constitution, [`.specify/memory/constitution.md`](.specify/memory/constitution.md) — it wins. This file is the pointer and gotcha list; [`docs/`](docs/claude/app-state.md) holds current state.

## Reference Docs (open when relevant)

- [Money semantics](docs/money-semantics.md) — source of truth for every money calculation; a PR changing money math updates it and [the finance framework](docs/finance-framework.md) in the same diff
- [App state, stack, layout, conventions](docs/claude/app-state.md) — stack, how to run, project layout, test strategy, key patterns (CQRS, migrations ordering, auth, encryption), branch/commit/PR/release naming, collaboration style, platform-first list, infrastructure-catalog rule
- [QA guide](docs/claude/qa.md) — test creds, golden-path scenarios, e2e process
- [AI pipeline](docs/claude/ai-pipeline.md) · [Speckit agent context](docs/claude/speckit-context.md) (machine-owned; never hand-grow) · [Operations runbook](docs/OPERATIONS_RUNBOOK.md) · [MCP tools](docs/mcp.md) · [Roadmap](specs/ROADMAP.md)

## Gotchas (each cost a real session)

- `dotnet build --no-restore` fails on a fresh clone — always restore first.
- Never change `Host=postgres` to `localhost`, or edit connection strings/env config, to work around Docker — fix the root cause.
- Do not create markdown files at the repo root; only `README.md`, `AGENTS.md`, `CLAUDE.md`.
- A bare `.dockerignore` pattern matches only at the context root in this BuildKit version, so nested outputs are `**/<name>` (`**/bin`, `**/obj`, `**/node_modules`, ...; full list and symptoms in [app-state](docs/claude/app-state.md)). A miss shows up as a stray `obj/` over a fresh restore (a confusing NETSDK1064 "package not found") or the runner's `frontend/node_modules` entering the image (Frontend CI builds `Dockerfile.frontend.prod` in the same job as `npm ci`).
- `docker/Dockerfile`, `Dockerfile.mcp`, `Dockerfile.gateway` set `NUGET_PACKAGES=/src/.nuget/packages` and restore into the build layer instead of a `--mount=type=cache` mount, which silently dropped packages between restore and publish under BuildKit GC.
- `dotnet tool restore` needs its manifest at the repo root `.config/dotnet-tools.json`, not under `backend/`.
- The frontend `initial` bundle budget in `frontend/angular.json` is a ratchet: raise it deliberately, never to silence a warning. Changes to `docker/nginx.frontend.conf` / `Dockerfile.frontend.prod` trigger the Frontend CI LCP gate.
- A PR title must carry the highest-ranked commit type (`feat` > `fix` > `perf`/`refactor` > `docs` > `chore`): squash makes it main's only message (PR #550 merged a `feat` branch under a `docs` title). Release PR and version bumps go through the `lifekit-release-bot` app token — never hand-bump or tag (#623).
- A contract-test `*Shape` record gaining a field needs the matching `*Result` field, or JSON deserializes it to null silently (`ConnectBinanceResult.Message`).
- Migrations run module-by-module, not by timestamp: a cross-schema migration needs the split-`Migrate(target)` pattern (details in app-state).

## Memory (vault)

Durable knowledge about this project lives in the vault, not in provider memory: `~/memory/projects/finance-sentry/` - `plan.md` (facts), `STATUS.md` (in-flight, only while parked), `log.md` (dated events). Read `plan.md` + `STATUS.md` when starting work here; verify live state live (`gh`, `docker ps`). Write durable decisions and gotchas back to those pages in the same session (contract: `~/memory/README.md`). Claude Code auto-memory is disabled by policy (`CLAUDE_CODE_DISABLE_AUTO_MEMORY=1`); Codex/other agents follow the same pointer via this file.
