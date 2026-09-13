# Tasks: Remove devclaw sandbox lore from the repo

**Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Issue**: #557

## US1 — AGENTS.md carries only checkable facts

- [x] T001 Strip the Playwright environment prelude (`LD_LIBRARY_PATH`, `cp … libXfixes.so.3`) and
      the "Frontend test environment gotcha" section from `AGENTS.md`.
- [x] T002 Delete the "sandbox shortcut" recipe (clone lifekit-common, build `@lifekit-hq/*`, install
      tarballs, hand-run the patch script); state `NODE_AUTH_TOKEN` as the real prerequisite instead.
- [x] T003 Replace the `--no-verify` guidance with the pre-commit hook's actual contract, as the
      repo's own rule (decision in plan.md: hook kept as-is).
- [x] T004 Delete the "Frontend pre-commit version-bump gate" section — `.husky/pre-commit` removed
      that gate on purpose; release-please owns the version.
- [x] T005 Delete the "Frontend UI primitives — ng-zorro-antd" section — neither the dependency nor
      `frontend/projects/dsdevq-common/` exists.
- [x] T006 Replace the drifted module/test enumerations and the MCP run snapshot with pointers, so
      `AGENTS.md` is back to roughly a page.
- [x] T007 Execute the documented sequence verbatim on a clean checkout: `cd frontend && npm ci &&
      npm run lint && npm run format:check && npm run build && npx playwright test` — green, 32/32
      e2e specs, with no environment prelude.

## US2 — Retire the `@lifekit-hq/ui` monkey-patch *(blocked — human-owned dependency bump)*

- [ ] T008 Bump `@lifekit-hq/ui` (and its `@lifekit-hq/*` siblings) from `^0.2.0` to `^0.3.1` in
      `frontend/package.json`; refresh `frontend/package-lock.json`.
- [ ] T009 Delete the `postinstall` script line and `frontend/scripts/patch-lifekit-ui.js`.
- [ ] T010 Re-run the frontend sequence; `e2e/net-worth-chart-toggle.spec.ts` must pass against the
      unpatched library.

T008–T010 touch verification inputs (package manifest, lockfile, install script) that agents do not
edit; the library prerequisite is already published (see plan.md).
