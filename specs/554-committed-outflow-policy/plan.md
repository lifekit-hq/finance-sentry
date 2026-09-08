# Plan: One Committed-Outflow Policy (554)

## Architecture decisions

| Decision | Choice | Why |
|---|---|---|
| Where the definition lives | New `ICommittedOutflowPolicy` + `CommittedOutflowRules` in `BankSync/Application/Services` | #554 AC1 asks for one policy every consumer calls. It cannot be an extension of `CommitmentKeyResolver`: that type is a pure static key derivation with no user scope, and rules (a)/(d) need a per-user set read from a port |
| Policy shape | `Task<CommittedOutflowRules> LoadForUserAsync(userId, ct)` returning an immutable per-user rule set with the predicates on it | Precedent: `ICounterpartyClassificationService.ClassifyForWindowAsync` — an async service that loads once per request and hands back a value object every consumer shares. A per-debit `Task<bool>` would issue one round trip per transaction |
| Two predicates, one type | `IsCommitted(Transaction)` for rules (a)/(b)/(d) and `IsCommittedFlowRole(string)` for rule (c) | The reader classifies at two granularities — per debit in the normal pass, per role on the synthetic counterparty row — because counterparty transactions are excluded from the per-debit pass upstream. Both live on one type so the four rules read together in one file |
| Rule (c) source | `FlowRoles.FamilySupport` and `FlowRoles.Household` from spec 044's classification | AC2 forbids a second family-flow heuristic. `household` joins `family_support` because it is what it says: a recurring household obligation paid as a transfer (the mortgage) — a bill, which is the definition of committed |
| Rule (b) constant set | Private static `CommittedCategories` on `CommittedOutflowRules`, not a new `CategoryKeys` member | `CategoryKeys` is the shared-kernel vocabulary of what a category IS; which categories are *committed* is this policy's opinion and belongs where the other three rules are |
| Rule (b) over-claim | Accepted and pinned by a test, not carved out | `RENT_AND_UTILITIES` is wider than rent: the 4812–4900 telecom MCC range and the `top-up` keyword land there, so an ad-hoc mobile top-up reads as committed. Narrowing by MCC inside the policy would put a second categorization opinion beside the ladder that owns categories (#553), and no statement line distinguishes a plan payment from a top-up anyway |
| Rule (d) in this increment | Not stubbed — no field, no empty set, no branch | An always-false clause is placeholder code. US2 adds the clause together with the store that feeds it |
| `MoneyFlowStatisticsService` dependency | `ICommittedOutflowPolicy` replaces `IActiveSubscriptionsReader` outright | AC1: consumers call the policy rather than re-deriving. Leaving the reader injected would leave two ways to ask the same question |
| Where `CommitmentKeyResolver` now runs | Inside `CommittedOutflowRules.IsCommitted` | The resolver stays the key derivation it is; the policy is the only caller that turns a key into a verdict |
| Discretionary derivation | Unchanged: `OutflowUsd − CommittedOutflowUsd`, on both the per-currency and the synthetic row | Keeps the partition exact under rounding (045 decision, still load-bearing) |

## Story-slice surfaces

### [US1] Policy + rules (a)/(b)/(c) — files touched / created

- `FinanceSentry.Modules.BankSync/Application/Services/CommittedOutflowPolicy.cs` — **new**;
  `ICommittedOutflowPolicy`, `CommittedOutflowPolicy`, `CommittedOutflowRules` with the four
  rules documented in XML doc (rule (d) named as US2's).
- `FinanceSentry.Modules.BankSync/Application/Services/MoneyFlowStatisticsService.cs` — ctor
  takes the policy instead of the reader; the per-debit committed sum and the synthetic
  counterparty row both ask the rule set; interface XML doc rewritten around the four rules.
- `FinanceSentry.Modules.BankSync/BankSyncModule.cs` — `AddScoped<ICommittedOutflowPolicy, …>`
  next to the other statistics services.
- `tests/FinanceSentry.Tests.Unit/BankSync/Application/CommittedOutflowPolicyTests.cs` —
  **new**; the rules in isolation.
- `tests/FinanceSentry.Tests.Unit/BankSync/Application/MoneyFlowStatisticsTests.cs` — the 5+
  construction sites move to a policy stub; new fixture tests for rent, family support and the
  partition invariant on the synthetic row.
- `docs/money-semantics.md` §5a — the definition is now four rules, not one.

Constraint discovered while planning: counterparty-matched transactions never reach the
per-debit pass (`MoneyFlowStatisticsService` step 6 filters `matchedIds` out before grouping),
so rule (c) *must* be a flow-role predicate applied to the synthetic row — a per-debit-only
policy would classify family support as discretionary no matter how it was written.

Second constraint: `Counterparty.FlowRole` defaults to `string.Empty`, so a counterparty row
saved without a role is real spending with no recognised role. It is in `realFlows` (only
`investment` and `self_routing` are carved out) and must fall to discretionary — that is the
live path that keeps the role predicate from being a tautology over today's role set.

### [US2] Rule (d) — user pins (NOT built; recorded for the next session)

- `FinanceSentry.Modules.BankSync/Domain/CommittedMerchantPin.cs` + repository port, EF config
  and a migration (precedent for a small per-user BankSync table: `Counterparty`)
- `Application/Commands/PinCommittedMerchant*` / `UnpinCommittedMerchant*`, a list query
- `CommittedOutflowPolicy` loads the pin set alongside the commitment keys; `IsCommitted` gains
  the `NormalizeDetectionKey` clause
- `API/Controllers` endpoint + an MCP tool (register in `ToolNameContractTests`' canonical list)
- Tests: repository round-trip, policy clause, contract test on the endpoint and the tool
