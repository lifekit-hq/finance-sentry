# Implementation Plan: Remove devclaw sandbox lore from the repo

**Branch**: `goal/fs-557-remove-sandbox-lore-2026-09-03` | **Date**: 2026-09-09 |
**Spec**: [spec.md](spec.md) | **Issue**: #557

## Approach

The debt is prose, not behaviour: `AGENTS.md` documents one machine's workarounds as if they were
Finance Sentry's build contract. The fix is to delete what is false and to *prove* what replaces
it by running the documented sequence verbatim on this checkout — a doc claim nobody executed is
how the lore got in.

## Load-bearing decisions

- **The pre-commit hook keeps `npm run lint` + `npm run format:check` and gains nothing** (done-when
  3). It already does not run `npm ci`; on a checkout with `frontend/node_modules` present it is
  seconds, and it mirrors the Frontend CI `lint-build-test` job, so it catches the exact failure CI
  would. The only cost is the `eslint … ENOENT` a contributor hits when `node_modules` is absent —
  the answer to that is `npm ci`, not `--no-verify`. Recorded in `AGENTS.md`'s commands section as
  the repo's rule.
- **AGENTS.md points, it does not enumerate.** The module list under "Project layout" had drifted
  (10 of 22 backend projects missing) because it duplicated `ls backend/src`. Directories are named
  once with their role; the tree is the source of truth. Same for the MCP tool catalogue, which
  `docs/mcp.md` and the tool-name contract test already own.
- **`devclaw.json`'s `verifyCmd` is left alone.** It already runs the plain project commands, so
  the lore never reached the executed gate — only the prose a human reads.

## Story slices

### US1 — AGENTS.md carries only checkable facts *(this increment)*

Files: `AGENTS.md` only. Constraint discovered: five distinct false claims, not three —
`LD_LIBRARY_PATH`/`libXfixes`, the tarball "sandbox shortcut", `--no-verify`, a
"Frontend pre-commit version-bump gate" the hook deliberately removed (release-please owns
versions — see the comment block in `.husky/pre-commit`), and a "Frontend UI primitives —
ng-zorro-antd" section describing `@dsdevq-common/ui` under `frontend/projects/`: no such
dependency, no such directory. Verification is executing the documented sequence, unmodified.

### US2 — Retire the `@lifekit-hq/ui` monkey-patch *(blocked: needs a human-owned dependency bump)*

Files: `frontend/package.json` (drop `postinstall`, bump `@lifekit-hq/ui`),
`frontend/package-lock.json`, delete `frontend/scripts/patch-lifekit-ui.js`.

The library side is **done**: `@lifekit-hq/ui@0.3.1` on GitHub Packages declares
`readonly stacked: InputSignal<boolean>` on both `AreaChartComponent` and `BarChartComponent`
(verified by unpacking the published tarball on 2026-09-09) — lifekit-hq/lifekit-common#23 shipped.
This repo pins `^0.2.0`, a range that cannot resolve 0.3.x, so the patch is still load-bearing
until the pin moves.

Remaining work is exactly: pin `^0.3.1`, refresh the lockfile, delete the `postinstall` line and
the script, then run `npm ci && npm run lint && npm run build && npx playwright test` — the toggle
proof is the existing `e2e/net-worth-chart-toggle.spec.ts`. All four files are verification inputs
this repo's automation guards against agent edits, so the slice is handed to a human rather than
worked around.

## Verification

`cd frontend && npm ci && npm run lint && npm run format:check && npm run build &&
npx playwright test` — the sequence as `AGENTS.md` now states it, with no environment prelude.
Run on 2026-09-09: install clean, lint clean, format clean, build clean (the pre-existing 1 MB
bundle-budget warning aside), 32/32 e2e specs passed.
