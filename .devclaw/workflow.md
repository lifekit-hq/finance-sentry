# How work is planned, built and shipped in finance-sentry

Read this first, then `AGENTS.md` (exact commands, layout, gotchas) and `CLAUDE.md`
(conventions, current state). The constitution at `.specify/memory/constitution.md`
wins over both on architecture, testing and code-quality gates.

## Planning

The project uses **speckit** (`.specify/`), driven from Claude Code skills:
constitution → `/speckit.specify` → `/speckit.clarify` → `/speckit.plan` →
`/speckit.tasks` → `/speckit.implement`. Artifacts land in
`specs/<nnn>-<slug>/` (`spec.md`, `plan.md`, `tasks.md`, `research.md`,
`data-model.md`, `quickstart.md`).

Not every change earns a spec. A bug fix or a single-PR increment plans in the
**PR description** — one reviewable increment per session. Reach for speckit only
when the ticket is a feature with more than one PR in it; `specs/ROADMAP.md`
holds the destination and the unimplemented specs.

Load-bearing decisions (a schema, an API shape, a money rule) go where the
feature's artifacts live: the `specs/<nnn>-…/` page for a spec'd feature, the PR
body otherwise. Money math additionally updates `docs/money-semantics.md` **in
the same diff** — that file is the source of truth for every money calculation.

## Build / test / verify

`.devclaw/verify` runs what Backend CI runs: restore → `dotnet build` (Release) →
`dotnet test` over the **whole solution, unfiltered**. Everything is serialised
with `-m:1`; the default parallel run gets OOM-killed inside the sandbox.

Not covered by that script, run by hand when the change touches them:

- **Frontend CI** — `npm ci && npm run lint && npm run format:check && npm run build
  && npm run test:ci && npx playwright test` in `frontend/`. Needs
  `NODE_AUTH_TOKEN` (read:packages) for `@lifekit-hq/*`; without it, follow the
  tarball route in AGENTS.md. Also needs `libXfixes.so.3` copied to `/tmp`.
- **Postgres-backed tests** — CI provides a `postgres:14-alpine` service; locally
  they skip (`[DockerRequiredFact]`, or a connection failure). A skip is not a pass:
  a change to EF mappings or migrations is only proven by the Postgres-backed test,
  so say so in the PR when you could not run it.
- **Coverage ratchet** — backend floor 25% line, enforced in CI only. Frontend
  floors live in `angular.json` (test → `ci`).

Gates that are not yours to edit: `.github/workflows/**`, `AGENTS.md`,
`.husky/**`, `frontend/.npmrc`, `global.json`, `Directory.Build.props`,
`Directory.Packages.props` — unless the ticket names them.

## Conventions

- **Branch**: `<type>/<issue#>-<slug>`, created with `gh issue develop <n> -b`.
- **Commit / PR title**: conventional commits, scope = module or spec number. A
  squashed PR's title must carry the *highest-ranked* type among its commits
  (`feat` > `fix` > `perf`/`refactor` > `docs` > `chore`) — the title becomes
  main's only commit message and release-please parses it.
- **PR body**: what + why, then a **Validation** section naming exactly what was
  run and its result (`.github/PULL_REQUEST_TEMPLATE.md` scaffolds it).
- Main is protected; everything lands via squash-merge PR with CI green.
- Releases are release-please's job — never hand-bump a version or tag. The
  pre-commit hook deliberately does **not** gate on a version bump (`.husky/pre-commit`
  explains why it was removed); AGENTS.md still claims it does, and is stale there.
- Infrastructure changes (container, cron, workflow, external service, secret)
  need a companion PR to `lifekit-dashboard/backend/infra.json`.
- No new markdown files at the repo root — only `README.md` and `CLAUDE.md`.

## Traps

Durable repo gotchas live in `.devclaw/memory/`, indexed in `.devclaw/MEMORY.md`.
Read a fact when its hook bears on your task; add one (never a reworded sibling)
when you learn something a future session on a different task would need.
`.devclaw/trends.md` is written by devclaw's trend detector — observations, not
instructions.
