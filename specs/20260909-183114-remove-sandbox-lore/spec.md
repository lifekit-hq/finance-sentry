# Feature Specification: Remove devclaw sandbox lore from the repo

**Feature Branch**: `goal/fs-557-remove-sandbox-lore-2026-09-03`

**Created**: 2026-09-09

**Status**: US1 implemented; US2 blocked on lifekit-common#23

**GitHub Issue**: #557

## Context

An audit on 2026-09-03 found that a worker's *sandbox* environment had been persisted into this
product repo as if it were a fact about Finance Sentry. Three artefacts:

1. `AGENTS.md` teaches every future contributor (human or agent) to run Playwright with
   `LD_LIBRARY_PATH=/tmp:$LD_LIBRARY_PATH` after copying `libXfixes.so.3` out of
   `/usr/lib/aarch64-linux-gnu/`, and offers a "sandbox shortcut" that clones lifekit-common,
   builds `@lifekit-hq/*` from source and installs tarballs instead of using the registry.
   Neither is true of this project: the dev image ships Chromium's system libraries, and the
   GitHub Packages token is a declared capability.
2. `frontend/package.json`'s `postinstall` runs `scripts/patch-lifekit-ui.js`, a string-replace
   over `node_modules/@lifekit-hq/ui`'s FESM bundle and `.d.ts` that grafts a `stacked` input the
   published library never shipped. Production builds depend on a monkey-patched dependency, and
   because `String.replace` no-ops on a non-matching needle while the script prints "patched"
   either way, drift is silent.
3. `AGENTS.md` presented `--no-verify` as needed because the pre-commit hook supposedly runs
   `npm ci`. It does not — the hook runs lint-staged, `npm run lint` and `npm run format:check`.
   The convention was invented to route around a sandbox, never decided for this repo.

Two further AGENTS.md sections were found false in passing and are part of the same debt: a
"Frontend pre-commit version-bump gate" (the hook removed that gate deliberately — release-please
owns versions) and a "Frontend UI primitives — ng-zorro-antd" section describing
`@dsdevq-common/ui` under `frontend/projects/`, a package and a directory that no longer exist and
a dependency this repo does not have.

## Principle

`AGENTS.md` is the contract a fresh contributor reads before touching anything. Every line in it
must be checkable against the repo *today*. A line that describes one machine's workaround is
worse than no line: it gets followed, it spreads, and it hides the real environment requirement
(here: `NODE_AUTH_TOKEN`) behind a ritual.

## User Scenarios & Testing

### US1 — A contributor reads AGENTS.md and gets the plain project commands (P1)

A contributor (human or agent) opening `AGENTS.md` sees the frontend verify sequence as
`npm ci && npm run lint && npm run build && npx playwright test` with no library-path prelude, no
tarball-building shortcut and no `--no-verify` advice, and a commands section stating the repo's
own rule for the pre-commit hook. Every remaining statement in the file is true of the current
tree.

**Acceptance**

- `AGENTS.md` contains no `LD_LIBRARY_PATH`, no `libXfixes`, no "sandbox shortcut", no
  `--no-verify`.
- The pre-commit hook's actual contract (lint-staged + lint + format:check; **no** `npm ci`, **no**
  version bump) is stated once, as the repo's rule, in the commands section.
- No section describes a package, path or dependency absent from the tree.
- The file stays a thin pointer — roughly a page — linking out rather than inlining.
- The documented sequence is executed end-to-end, unmodified, on a clean checkout.

### US2 — The frontend installs the real `@lifekit-hq/ui` (P1, blocked)

`npm ci` performs no post-install surgery on `node_modules`; the dashboard's Stacked/Lines toggle
binds a `stacked` input the published library declares, and `frontend/scripts/patch-lifekit-ui.js`
is gone.

**Acceptance**

- `frontend/package.json` has no `postinstall`; `frontend/scripts/patch-lifekit-ui.js` is deleted.
- The pinned `@lifekit-hq/ui` release declares `stacked`.
- `npx playwright test` covers the Stacked/Lines toggle against the unpatched library.

**Blocked**: the `stacked` input must ship from lifekit-common first
(lifekit-hq/lifekit-common#23). Until that release exists, deleting the patch regresses the
dashboard toggle, and the dependency bump plus `postinstall` removal are edits to
`frontend/package.json` + `package-lock.json` — files this repo's automation guards as
verification inputs. This slice needs the library release and a human-owned dependency bump.

## Out of scope

- Changing the pre-commit hook's behaviour. It is reviewed in this ticket and kept as-is; the
  decision is recorded, the hook is not rewritten.
- Any change to CI workflows or to `devclaw.json`'s verify command — the lore lives in prose, and
  the verify command already runs the plain project commands.
