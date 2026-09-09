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

### [US2] Rule (d) — user pins (BUILT)

- `Domain/CommittedMerchantPin.cs` + `ICommittedMerchantPinRepository` (in `IRepositories.cs`),
  `CommittedMerchantPinRepository`, EF config in `BankSyncDbContext`, migration `M017`
- `Application/Commands/PinCommittedMerchantCommand.cs` / `UnpinCommittedMerchantCommand.cs`,
  `Application/Queries/ListCommittedMerchantPinsQuery.cs`,
  `Application/Services/CommittedMerchantKey.cs`
- `CommittedOutflowPolicy` loads the pin set alongside the commitment keys; `IsCommitted` gains
  the `NormalizeDetectionKey` clause
- `API/Controllers/CommittedMerchantsController.cs`, `API/Responses/CommittedMerchantPinDto.cs`
- `FinanceSentry.Mcp/Tools/CommittedMerchantsTool.cs` + `Responses/CommittedMerchantsToolResult.cs`,
  registered in `ToolNameContractTests`' canonical list (60 tools) and `docs/mcp.md`
- Tests: `CommittedMerchantPinsTests` (handlers over the real repository on an in-memory
  context), rule (d) cases in `CommittedOutflowPolicyTests`, a pinned-merchant split fixture in
  `MoneyFlowStatisticsTests`, `CommittedMerchantsAPIContractTests`, `CommittedMerchantsToolTests`

| US2 decision | Choice | Why |
|---|---|---|
| Pin identity | The normalized detection key, unique per `(UserId, MerchantKey)` | One pin has to claim every spelling the statement uses for the merchant; the typed name is kept as `DisplayName` for display only, since the key is lowercased and stripped |
| Where the key is derived | One `CommittedMerchantKey.Derive` seam, called by both handlers | The REST endpoint, the MCP tool and the policy cannot drift into three normalizations of one name |
| Rule (d)'s key vs rule (a)'s | Rule (d) re-derives with `NormalizeDetectionKey` instead of reusing `CommitmentKeyResolver.Resolve` | The resolver keys a repayment-shaped row `installment:{merchant}:{amount}`, which no merchant pin can equal — a pinned merchant's repayments would slip through. Pinned by a test that fails under the shared key |
| The `unknown` key | Refused on write (`UnpinnableMerchantException`, 400) **and** never matched on read | Every unnameable debit carries it, so one such pin would silently claim the whole unnamed tail of the book. Two guards because neither side should be able to cause a whole-book claim alone |
| Re-pinning | Idempotent — 200 + `AlreadyPinned`, not 409 | Two spellings normalize to one key, so a caller re-pinning has asked for a state that already holds |
| Unpin addressing | By merchant text on the query string, not by pin id in the path | The key is the pin's identity, so unpinning mirrors pinning and needs no listing round trip; a free-text key ("mobile top-up 0057") makes a brittle path segment |
| Unpin accepts two forms | ~~The derived key first, then the raw trimmed/lowercased input as a key~~ **Superseded by US3**: one derived key. The round-trip US2 bought with a second lookup path is now a property of the normalizer itself | Normalization was not a fixed point over every key it emits — pinning `"*MOBI TOP-UP 0857860057"` stored `mobile top-up 0057`, which re-derived to `mobile top-up`, so a derived-key-only lookup would 404 on the pin the listing just showed. US3 makes `f(f(x)) == f(x)` instead of carrying the second path |
| Pin matching | Exact key membership, never substring | A pin on a common word ("bank", "card") would otherwise swallow unrelated spend. The accepted cost: pins are inert on rows that name the merchant only inside a longer description (no `MerchantName`), which is pinned by a test and stated in §5a and the MCP tool description rather than left for a user to discover |
| DTO placement | `CommittedMerchantPinDto` in `Application/Queries`, not `API/Responses` | Module precedent (`GlobalTransactionDto` in `GetAllTransactionsQuery.cs`); an Application command referencing an API type is a layering inversion, and the MCP tool consumes the same shape without going through the API layer |
| MCP surface | One `committed_merchants` tool with an `action` parameter, not three tools | Keeps the 60-tool surface from growing by three for one concept; malformed calls come back as an `Error` member rather than a transport exception the agent cannot read |
| No frontend in this slice | REST + MCP only | The app renders no pin management yet; a UI slice is its own increment (see FOLLOW-UPS in the PR) |

Constraint discovered while building: `MerchantNameNormalizer.NormalizeDetectionKey` collapses
blank/punctuation-only input to `unknown`, the same key every unnamed debit carries — the pin
path had to reject it explicitly on both sides. Second: an installment-shaped row only reaches
rule (d) as its merchant when the statement line carries a `MerchantName`; keyed off the
description alone it normalizes to the whole description, which no pin equals.

### [US3] Hardening rule (d)'s seams — files touched

- `Application/Services/MerchantNameNormalizer.cs` — `NormalizeDetectionKey` becomes a fixed
  point over its own output; `Normalize` strips domain suffixes until stable.
- `Application/Commands/UnpinCommittedMerchantCommand.cs` — the raw second lookup path goes.
- `Application/Commands/PinCommittedMerchantCommand.cs` — one race-safe repository call.
- `Application/Services/CommittedOutflowPolicy.cs` — `CommittedOutflowRules` construction.
- `Domain/Repositories/ICommittedMerchantPinRepository.cs` — **new**; moved out of
  `IRepositories.cs`, gains `AddIfAbsentAsync` and loses `FindAsync`.
- `Infrastructure/Persistence/Repositories/CommittedMerchantPinRepository.cs` — the race catch.
- Tests: `MerchantNameNormalizerTests` (the idempotence property), `CommittedMerchantPinsTests`
  (the two race paths), `CommittedOutflowPolicyTests` + `MoneyFlowStatisticsTests` (construction).
- `docs/money-semantics.md` §5a — unpin takes one key form, not two.

| US3 decision | Choice | Why |
|---|---|---|
| Fixed point, not a second path | `NormalizeDetectionKey` recognises the `mobile top-up NNNN` key it emits and returns it unchanged | The mobi branch was the only clause whose output re-normalized to something else. Fixing the function removes the reason unpin had two lookups, rather than leaving one seam declared canonical and a second one live beside it |
| Suffix stripping repeats | `Normalize` strips `.com`/`.io`/… in a loop instead of once | `"x.com.com"` was the other input whose key was not stable under a second pass. Cheap, and the property test now covers all inputs rather than all-but-two |
| Two same-typed sets | `CommittedOutflowRules` takes `required` init properties, not positional args | Swapping the two `IReadOnlySet<string>` arguments compiled and silently inverted rules (a) and (d). Precedent for `required init`: `IbkrOAuthCredentials` |
| Where the pin race is handled | Repository `AddIfAbsentAsync`, catching `DbUpdateException` and re-reading | Precedent: `CompanionEventRepository.InsertIfNewAsync`. The unique index is an Infrastructure fact; the handler should not learn EF exception types to honour a documented idempotent result |
| Only a real duplicate is swallowed | The catch re-reads and rethrows when no winning row exists | Otherwise a genuine write failure would report a pin the user does not hold |
| `FindAsync` leaves the port | Folded into `AddIfAbsentAsync`; the impl keeps it private | Its only caller was the racy find-then-add; leaving it on the port invites the same shape back |
| Persisted keys are rekeyed, not left to drift | Data-only migrations `M006` (subscriptions) and `M018` (pins) strip the now-stripped domain suffix | Making the normalizer idempotent changes the key for one class of input: a suffix hidden behind trailing digits ("NETFLIX.COM 866-5797172" was `netflix.com`, is `netflix`). Rows minted under the old derivation would stop matching rule (a) AND get a duplicate on the next detection run. Precedent: M004's plan rekey |
| The rekey deletes only detected rows | Collision losers go when `IsManual = false`; hand-entered rows stay | A detected row is regenerable; a row the user typed is theirs, and silently deleting it to tidy a key is not a repair |
| The canonical top-up key is matched case-insensitively | `MOBILE TOP-UP 0057` and `mobile top-up 0057` both key `mobile top-up 0057` | A case-sensitive guard would recognise only the lowercase spelling, so one statement line could key two ways — the fragmentation the mobi clause exists to prevent. Accepted, unmigratable side effect: a line spelled exactly `mobile top-up NNNN` (four digits) used to key `mobile top-up`; no stored key records which rows came from such a line, and the real statement carries the ten-digit `*MOBI TOP-UP` form |
| The rekey is narrow, not a SQL port of the normalizer | Only keys whose stripped form is demonstrably today's derivation are rekeyed: no `paypal*` prefix, no leading punctuation, nothing revealed underneath, no brand alias | Re-implementing a strip-until-stable loop in SQL is how a repair lands on a *second* wrong key. Everything outside the provable class keeps its key and heals as it does today — detection re-derives it next run |
| The subscriptions rekey never deletes | A row that collides keeps its old key | The loser can carry state detection would not restore (a dismissal, a term count, an end date), so "the detected row is regenerable" is not true enough to delete on. Pins are different: a pin has no state beyond its key, and an unrekeyed duplicate would be a listing entry the user cannot remove |
| One winner per collapsed key | `DISTINCT ON (UserId, target)` in the UPDATE, the rest deleted | `NOT EXISTS` alone is evaluated against the pre-statement snapshot: two old keys collapsing to one would both pass it and the second write would break the unique index, leaving the whole rekey unapplied on every boot |

Constraint discovered while building: the in-memory provider does not enforce unique indexes, so
the race path is covered with a `BankSyncDbContext` subclass that fails the first
`SaveChangesAsync` after a competing context commits the winning row — a Sqlite/Postgres-backed
test would need a package this test project deliberately does not carry.
