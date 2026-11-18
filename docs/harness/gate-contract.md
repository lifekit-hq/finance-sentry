# Gate contract

What the no-mistakes gate runs, frozen so the 20-run attribution compares like with like.

**Freeze rule:** a change to the no-mistakes version or to any model below starts a new measurement window and is logged in the change log at the bottom of this file with a date.

- Frozen: 2026-11-18
- no-mistakes version: v1.106.1
- Source: `no-mistakes --version`, `~/.no-mistakes/config.yaml` (`agent: auto`, `agent_config`, `review_agents`) and this repo's `.no-mistakes.yaml`, which sets no per-step model; read 2026-11-18.

## Scoped test rerun

**Rule:** the diff-scoped `commands.test` in `.no-mistakes.yaml` runs after every review fix round, because the Test step is ordered after Review (intent, rebase, review, test, document, lint, push, pr, ci), so it sees the branch as the last review fix left it; no extra configuration is needed or set.

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

## Change log

| Date | Change |
| --- | --- |
| 2026-11-18 | Initial freeze: v1.106.1, every step sonnet/high. |
