# Gate contract

What the no-mistakes gate runs, frozen so the 20-run attribution compares like with like.

**Freeze rule:** a change to the no-mistakes version or to any model below starts a new measurement window and is logged in the change log at the bottom of this file with a date.

- Frozen: 2026-11-18
- no-mistakes version: v1.106.1
- Source: `no-mistakes --version`, `~/.no-mistakes/config.yaml` (`agent: auto`, `agent_config`, `review_agents`) and this repo's `.no-mistakes.yaml`, which sets no per-step model; read 2026-11-18.

## Scoped test rerun

**Rule:** the diff-scoped `commands.test` in `.no-mistakes.yaml` runs once after the review step ends, on the branch as the last review fix round left it (the Test step is ordered after Review: intent, rebase, review, test, document, lint, push, pr, ci); a rerun after each individual review fix round is not available by configuration alone, since the pipeline has no setting for it, so that is the missing capability, and this change sets no config.

Not covered: fixes the document and lint steps make run after Test and are not retested by the gate (GitHub CI is the check for those). Proof runs, both on the default-branch `.no-mistakes.yaml` since this change adds no config: run `01M7R4K520HG8N2TFJQ7DJVPWS` (PR #1502; review fix round 2 at 1795033370, Test started 1795034528) and run `01M7PY6X3Y8TEPVFG1GD04HAEN` (PR #1478; no fix round, Test ran once).

## Models per step

The global default is `agent: auto`, which resolves at run time to the first available native agent; on this host that is `claude`, and the table assumes that resolution. A different resolution changes the models without any edit here, so it counts as a change under the freeze rule. `agent_config.claude`: model `sonnet`, effort `high`. No step has a per-step override.

| Step | Model line |
| --- | --- |
| intent | model: sonnet (agent claude, effort high) |
| rebase | model: sonnet (agent claude, effort high) |
| review | model: sonnet (agent claude, effort high) |
| test | model: sonnet (agent claude, effort high) |
| document | model: sonnet (agent claude, effort high) |
| lint | model: sonnet (agent claude, effort high) |
| push | model: sonnet (agent claude, effort high) |
| pr | model: sonnet (agent claude, effort high) |
| ci | model: sonnet (agent claude, effort high) |

Review fixers (`review_agents`): `fixer` and `fixer_after_round` (from round 2) are both agent claude, model sonnet, effort high.

## Rigor lanes by stake

**Status: declared here, not selectable by configuration.** The pipeline cannot pick a lane from `.no-mistakes.yaml`. Missing: a per-path step selector. `path_instructions` only injects review text for matching files, `protected_paths` only refuses a run on a touched path, and `commands.test` already scopes itself by diff, but nothing maps a path glob to a step list. Skipping steps exists only as the per-run `--skip` flag the person starting the run passes. So this change sets no config and the gate is unchanged: every change still runs the full gate (intent, rebase, review, test, document, lint, push, pr, ci). The table below is the contract a future selector, or an operator choosing `--skip`, must follow.

**Invariant:** a path in doubt falls into the full gate. The light lane is the closed list below; every other path, including any new one, is full. No path is in two lanes (checked by `tests/harness/test_lanes.py`, which also checks each glob matches a tracked file).

| Lane | What runs |
| --- | --- |
| full | The whole gate, unchanged: intent, rebase, review, test, document, lint, push, pr, ci |
| light | lint and ci only (the other steps skipped) |

Lane table (one glob per row; `**` crosses directories, `*` does not):

| Lane | Reason | Glob |
| --- | --- | --- |
| full | money: the written money rules | `docs/money-semantics.md` |
| full | money: the finance framework the rules derive from | `docs/finance-framework.md` |
| full | money: multi-currency guide | `docs/MULTI_CURRENCY_GUIDE.md` |
| full | money: Claude rule that mirrors the money rules | `.claude/rules/money-semantics.md` |
| full | money: money-semantics case tests | `backend/tests/FinanceSentry.Tests.Unit/MoneySemantics/**` |
| full | money: currency conversion and return maths | `backend/src/FinanceSentry.Core/Utils/CurrencyConverter.cs` |
| full | money: money-weighted return | `backend/src/FinanceSentry.Core/Measures/MoneyWeightedReturn.cs` |
| full | money: Money value object | `backend/src/FinanceSentry.Modules.BankSync/Domain/ValueObjects/Money.cs` |
| full | money: money-flow statistics | `backend/src/FinanceSentry.Modules.BankSync/Application/Services/MoneyFlowStatisticsService.cs` |
| full | money: the single shared money pipe lint rules | `frontend/eslint-rules/money-*.mjs` |
| full | authentication: auth module | `backend/src/FinanceSentry.Modules.Auth/**` |
| full | authentication: API authentication | `backend/src/FinanceSentry.API/Authentication/**` |
| full | authentication: MCP authentication | `backend/src/FinanceSentry.Mcp/Authentication/**` |
| full | authentication: core auth | `backend/src/FinanceSentry.Core/Auth/**` |
| full | authentication: infrastructure auth | `backend/src/FinanceSentry.Infrastructure/Auth/**` |
| full | authentication: frontend auth module | `frontend/src/app/modules/auth/**` |
| full | migrations: module migrations run module by module, not by timestamp | `backend/src/*/Migrations/**` |
| full | migrations: migration tests | `backend/tests/FinanceSentry.Tests.Unit/Migrations/**` |
| full | provider sync: bank sync | `backend/src/FinanceSentry.Modules.BankSync/**` |
| full | provider sync: brokerage sync | `backend/src/FinanceSentry.Modules.BrokerageSync/**` |
| full | provider sync: crypto sync | `backend/src/FinanceSentry.Modules.CryptoSync/**` |
| light | documentation: harness docs | `docs/harness/**` |
| light | documentation: agent docs | `docs/claude/**` |
| light | documentation: design docs | `docs/design/**` |
| light | documentation: working notes | `docs/superpowers/**` |
| light | documentation: specs | `specs/**` |
| light | documentation: readme | `README.md` |
| light | chore: dependabot config | `.github/dependabot.yml` |

Proof: no run of the light lane exists, since the pipeline has no way to run one by configuration; the allowed and blocked cases the test covers are the glob checks (a fixture glob matching nothing, and a fixture path in two lanes, are each rejected).

## Change log

| Date | Change |
| --- | --- |
| 2026-11-18 | Initial freeze: v1.106.1, every step sonnet/high. |
