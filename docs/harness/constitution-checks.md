# Constitution checks

The constitution (`.specify/memory/constitution.md`, Code Review & Compliance) lists twelve rules that used to end in "automatic block". Most had no automatic check, so the phrase was prose standing in for a mechanism. This table is the audit: one row per rule, the check that enforces it today, and the decision. The constitution tags each rule `CC1`–`CC12` and names the check or says **Guidance**.

**Decisions:** *keep as is* (a named check enforces it), *rewrite as guidance* (no check; the constitution says what the author and reviewer do), *needs a new check* (filed as its own issue, not built here). A rule that is partly enforced says so in the row; its constitution bullet names the check for the enforced part and marks the rest Guidance.

Enforced by earlier exercises: warnings as errors (CC9, #1478) and the file-organisation and provider-extraction lint rules (CC7 #1496, CC6 provider part #1499). The shared-boundary lint rule landed for CC8 (#1505).

The check cell is `none` or starts with the backticked name of the check; the constitution bullet for the rule must contain that name. `tests/harness/test_constitution.py` fails when a bullet still says "automatic block" next to a rule marked `none` here, and when a rule with a check does not name it.

| ID | Rule | Check today | Decision |
| --- | --- | --- | --- |
| CC1 | Failing linter checks | `Lint code` (frontend ESLint, `frontend-checks` job, Frontend CI), `Format check`, and backend analyzers in the Backend CI build | keep as is |
| CC2 | Missing or incomplete tests | none (a coverage floor exists, `Enforce coverage ratchet floor` in Backend CI and `test:ci` in Frontend CI, but it cannot tell whether a given change has tests) | rewrite as guidance |
| CC3 | Non-encrypted data handling | none | rewrite as guidance |
| CC4 | Coupling between modules | `ModuleBoundaryTests` and `NamespaceBoundaryTests` (Backend CI, `FinanceSentry.Tests.Unit`) | keep as is |
| CC5 | External integration accessed without a domain interface (Principle I) | none | rewrite as guidance |
| CC6 | Frontend feature state in a component, `setInterval` polling, inline error-code ladders, custom providers not under `core/providers/` (Principle VI) | `providers/provider-extraction` (#1499) for providers; `composition/no-component-state`, `composition/no-set-interval` and `composition/no-error-code-ladder` (ESLint, #1506) for the rest | keep as is |
| CC7 | Interface or constant defined inline in a component or service file (Principle VI.5) | `structure/file-per-concept` (ESLint, #1496) | keep as is |
| CC8 | Cross-module type, utility or enum not in `shared/` (Principle VI.5) | `boundary/shared-boundary` (ESLint, #1505; covers model, type, enum and utility files, not services, stores or components) | keep as is |
| CC9 | Backend `dotnet build` warnings not resolved (Principle II) | `TreatWarningsAsErrors` in `backend/Directory.Build.props` (#1478), built in Backend CI | keep as is |
| CC10 | Ledger boundary: tool surface other than MCP, hook payload carrying more than ids, endpoint/tool/field named after Ledger (Principle VII) | none | needs a new check (#1507); rewrite as guidance until it lands |
| CC11 | Version not bumped on frontend/API changes | none per PR: `release-please` bumps versions in the release PR, and hand-bumping is forbidden (AGENTS.md) | rewrite as guidance |
| CC12 | Tag not created for a version bump | none per PR: the release automation tags after the release PR merges | rewrite as guidance |

Real count: twelve rules (the plan counted twelve). Six end up with no automatic check (CC2, CC3, CC5, CC10, CC11, CC12), and CC1, CC4, CC6, CC7, CC8 and CC9 are enforced.

Not changed here: the "CI/CD Check" lines in the constitution's Versioning & Tagging Policy section still describe a per-PR version validation that does not exist (release-please does the bumping). Rule CC11 and CC12 and the Governance paragraph now say what is true; rewriting that policy section is a separate change.
