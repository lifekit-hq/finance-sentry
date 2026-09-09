# Tasks: One Committed-Outflow Policy (554)

## [US1] One policy decides committed vs discretionary — rules (a), (b), (c)

- [x] Add `ICommittedOutflowPolicy` + `CommittedOutflowPolicy` + `CommittedOutflowRules` (BankSync `Application/Services`), documenting all four rules and naming rule (d) as US2's
- [x] `CommittedOutflowRules.IsCommitted(Transaction)` — rule (a) via `CommitmentKeyResolver`, rule (b) via the committed-category set
- [x] `CommittedOutflowRules.IsCommittedFlowRole(string)` — rule (c) over `FlowRoles.FamilySupport` / `FlowRoles.Household`
- [x] `MoneyFlowStatisticsService` takes `ICommittedOutflowPolicy` instead of `IActiveSubscriptionsReader`; per-debit sum and the synthetic counterparty row both ask the rule set
- [x] Rewrite the match-rule XML doc on `IMoneyFlowStatisticsService` around the four rules
- [x] Register the policy in `BankSyncModule`
- [x] `CommittedOutflowPolicyTests` — each rule in isolation: active subscription key, installment plan key, rent category, loan category, an ordinary shop, the two committed roles, investment/self-routing/unset role
- [x] `MoneyFlowStatisticsTests` — construction sites moved to a policy stub; fixture tests for monthly rent, a family-support counterparty flow, an unroled counterparty flow, the partition invariant on the synthetic row, and a cross-currency rent case that would fail under a native sum
- [x] Update `docs/money-semantics.md` §5a — the definition is four rules
- [x] `dotnet build backend/FinanceSentry.sln -c Release -m:1` — no new warnings
- [x] `dotnet test backend/FinanceSentry.sln --no-build -c Release -m:1` — green
- [x] Commit spec artifacts + code

## [US2] Rule (d) — user-pinned committed merchants

- [x] `CommittedMerchantPin` entity + repository port + EF configuration + migration (`M017`)
- [x] Pin / unpin commands and a list query, keyed by `MerchantNameNormalizer.NormalizeDetectionKey`
      through the one `CommittedMerchantKey.Derive` seam
- [x] `CommittedOutflowPolicy` loads the pin set; `IsCommitted` gains the rule (d) clause
- [x] REST endpoint (list / add / remove) on `CommittedMerchantsController`
- [x] `committed_merchants` MCP tool over the same commands, added to the canonical tool list
- [x] Tests: repository round-trip, the policy clause, the split end to end through
      `MoneyFlowStatisticsService`, endpoint contract test, MCP tool contract test
- [x] `docs/money-semantics.md` §5a — rule (d); `docs/mcp.md` — the tool row
- [x] `dotnet build` + `dotnet test` green; commit spec artifacts + code

## [US3] Hardening rule (d)'s seams — one key derivation, one race-safe write

- [x] `MerchantNameNormalizer.NormalizeDetectionKey` is a fixed point over its own output
      (canonical `mobile top-up NNNN` key recognised; domain suffixes stripped until stable)
- [x] `MerchantNameNormalizerTests` — the `f(f(x)) == f(x)` property over the book's real shapes
- [x] `UnpinCommittedMerchantCommand` drops the raw `.Trim().ToLowerInvariant()` second lookup;
      US2's unpin-by-the-advertised-key test still passes through the one seam
- [x] `CommittedOutflowRules` takes `required` init properties — the two same-typed key sets
      cannot be swapped at a call site
- [x] `ICommittedMerchantPinRepository` moves to its own file out of `IRepositories.cs`
- [x] `AddIfAbsentAsync` on the repository (precedent: `CompanionEventRepository.InsertIfNewAsync`)
      — concurrent pins of one merchant return `AlreadyPinned`; a non-duplicate
      `DbUpdateException` still surfaces; `FindAsync` leaves the port
- [x] Tests: both race paths, over a context that fails the first save after a competing commit
- [x] Rekey the persisted keys the idempotence fix moves: `M006` (detected subscriptions) and
      `M018` (pins) strip the domain suffix that used to hide behind trailing digits
- [x] `docs/money-semantics.md` §5a — unpin takes one key form
- [x] `dotnet build` + `dotnet test` green; commit spec artifacts + code
