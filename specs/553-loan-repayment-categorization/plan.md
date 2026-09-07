# Plan: Loan and Installment Repayments Categorized as LOAN_PAYMENTS (553)

## Architecture decisions

| Decision | Choice | Why |
|---|---|---|
| Where the rule lives | New `Application/Services/LoanRepaymentClassifier` — a static `Resolve(...) → string?` spliced into the categorization ladders | Issue AC1 requires one place in the resolver chain. Precedent: `TransferDescriptionClassifier.Resolve(description)` is exactly this shape and is already spliced into the Monobank ladder |
| Why not a method on `ICategoryResolver` | `CategoryResolver` is a **singleton** over a process-wide snapshot of the reference tables; the loan rule needs the **per-user** set of active plans | Putting per-user state behind a singleton cache would either leak one user's plans onto another or force a cache keyed by user with no invalidation. The rule needs no reference-table data at all |
| How the mortgage is recognised | `CommitmentKeyResolver.Resolve(...)` matches an active **`installment`-kind** plan on key **and** amount | The mortgage's description is a masked PAN — no wording exists to match. The detector already labels it `installment` (`MaskedPan.IsLikely` in `SubscriptionDetectionJob.DetectSubscriptions`), so the categorizer trusts that label instead of inventing a second masked-PAN heuristic |
| Why the amount is part of the match | `MerchantNameNormalizer.Normalize` strips the trailing group, so `516936******4992` stores as **`516936`** — the bare BIN, pinned at `SubscriptionDetectionAlgorithmTests.cs:253` | Every card issued on that BIN shares the mortgage's key. Key-only membership booked a ₴200 transfer to a friend's monobank card as a mortgage payment — a direct AC3 violation, caught in review. A plan only exists because its charges cluster (detector CV gate ≤ 0.10), so ±15% around the stored average is the faithful mirror; the port therefore returns `ActiveInstallmentPlan(Key, ExpectedAmount)`, not a key set |
| Where the classifier sits in the Monobank ladder | AFTER the runtime-editable keyword bridge, BEFORE `TransferDescriptionClassifier` and the MCC map | #582 gave the admin-editable `merchant_keywords` rows the last word on a wording, and #581 already seeds `погашення` / `monomarket` / `щомісячний платіж` → `LOAN_PAYMENTS`; putting the classifier in front would shadow rows an admin can no longer change. Ahead of the MCC map is all AC1 asks for, and ahead of the transfer-description classifier guarantees a repayment can never fall into `TRANSFER_OUT` |
| Description branch stays MCC-agnostic | `IsInstallmentTransaction` is consulted whatever the MCC | The issue's own evidence table has `Щомісячний платіж telemart - monomarket` on a *different* MCC already resolving `LOAN_PAYMENTS`; gating on 4829 would regress it, and the shipped `monomarket` keyword row is MCC-agnostic too |
| Why filter on kind rather than reuse `GetActiveCommitmentMerchantKeysAsync` | That set includes `subscription`-kind rows (Netflix, a recurring transfer to a person) | A recurring service is not a repayment obligation. Reusing the wider set would recategorize every detected subscription as `LOAN_PAYMENTS` — including the `Ліза ❤️` transfers the issue explicitly requires to stay `TRANSFER_OUT` |
| Cross-module read | New method on the existing `IActiveSubscriptionsReader` port (Core), returning `ActiveInstallmentPlan` records | The port exists, is implemented in Subscriptions, registered in `SubscriptionsModule`, and BankSync already consumes it from `MoneyFlowStatisticsService`. No new contract, no new adapter, no DI wiring |
| Debit-only gate | Inside the classifier: anything explicitly `"credit"` is rejected | Both call sites would otherwise re-derive the test and drift. Null `TransactionType` is treated as an outflow, matching `SubscriptionDetectionJob`'s `TransactionType == null \|\| == "debit"` filter over the same legacy rows |
| Where the plans are loaded | Once per sync / per recategorization run, then threaded into the per-row mapping | Both `MonobankAdapter` entry points and `RecategorizeUserAsync` already have `userId` in scope. A per-row async lookup would issue one query per transaction |
| Backfill | The existing `recategorize` verb — `ResolveFromRaw` consults the classifier before the MCC map | Issue AC2 names this path. Pass 2 (provider re-fetch) inherits the fix for free, because it takes its category from the adapter |
| No new migration | `M015` already repaired the wording-matched rows; the masked-PAN rows need the per-user plan set, which SQL has no access to | A migration would have to re-implement `CommitmentKeyResolver` in SQL and would go stale the moment the key derivation changes |

## Story-slice surfaces

### [US1] Loan repayments categorized at the source — files touched / created

- `FinanceSentry.Core/Interfaces/IActiveSubscriptionsReader.cs` — add
  `GetActiveInstallmentPlansAsync` + the `ActiveInstallmentPlan(Key, ExpectedAmount)` record
- `FinanceSentry.Modules.Subscriptions/Application/Services/ActiveSubscriptionsReader.cs` —
  implement it (active rows, `Kind == installment`, `MerchantNameNormalized` + `AverageAmount`)
- `FinanceSentry.Modules.BankSync/Application/Services/LoanRepaymentClassifier.cs` — **new**;
  the whole rule
- `FinanceSentry.Modules.BankSync/Infrastructure/Monobank/MonobankAdapter.cs` — inject the
  reader, load the plans once per sync, splice the classifier into the ladder behind the keyword bridge
- `FinanceSentry.Modules.BankSync/Application/Services/TransactionRecategorizationService.cs` —
  inject the reader, load the plans per user, consult the classifier before the MCC map
- `docs/money-semantics.md` — the repayment carve-out from the transfer exclusion
- tests: `LoanRepaymentClassifierTests` (new), `MonobankStatementContractTests` (ctor +
  ladder case), `TransactionRecategorizationServiceTests` (ctor + backfill case),
  `MoneyFlowStatisticsTests` (mortgage- and monomarket-shaped debits), `ActiveSubscriptionsReaderTests`

Constraints discovered while planning:

- `MonobankAdapter` is registered `AddScoped` three ways in `BankSyncModule` (as
  `IMonobankAdapter`, as itself, and as an `IBankProvider` factory), and
  `IActiveSubscriptionsReader` is also `AddScoped` — so the new dependency is lifetime-safe.
- Ingest sees a *new* mortgage row before `SubscriptionDetectionJob` has created its plan, so
  the very first repayment of a new plan lands `TRANSFER_OUT` until detection runs and the
  recategorization path re-resolves it. Inherent to a detector-driven rule; documented on the
  classifier rather than worked around.
- `MonobankStatementContractTests` constructs the adapter directly — the new ctor parameter has
  to be threaded through that fixture.
- The two paths do not agree on the keyword bridge: `ResolveFromRaw` has never consulted it for a
  row that carries an MCC (it short-circuits to `ResolveMcc`), so an admin keyword override of an
  installment wording holds at ingest and is re-claimed by the next `recategorize` run.
  Deferred out of US1 as pre-existing — **closed by US2**, which makes the backfill run the same
  ladder as ingest.

## Architecture decisions — US2

| Decision | Choice | Why |
|---|---|---|
| Where the shared order lives | New `ITransactionCategorizer` (Application/Services/CategoryMapping) + `TransactionCategorizer` (Infrastructure/Categorization) | Precedent: `ICategoryResolver` / `CategoryResolver` is exactly this interface-in-Application, impl-next-to-the-reference-tables split. The owner's steering allowed either "behind `ICategoryResolver`" or an explicit ordered pipeline; a pipeline was chosen because `CategoryResolver` is a **singleton over a process-wide table snapshot** and the loan rung needs the **per-user** installment plans — the US1 decision table already rejected putting per-user state behind that cache |
| Why not extend `ICategoryResolver` | Same reason, plus `ICategoryResolver` is a *reference-table reader*; the ladder is a *policy*. Merging them would give the singleton snapshot a per-call user-scoped argument | Keeps the cache invalidation story (`Refresh()`) about tables only |
| Signals as one record | `CategorizationSignals(Description, MerchantName, TransactionType, Amount, Mcc, ProviderCategory)` | Providers fill only what they have — Monobank an MCC and no provider category, TrueLayer the reverse — so one ladder serves both without a per-provider overload. Adding a future signal is one record field, not three call-site changes |
| Rung order | keyword → loan → provider category → transfer description → MCC | Keyword first is #582's precedent (the admin's override channel). Loan above the MCC map is US1/AC1. **Provider category above the transfer prefix** because a bank classifying "To Go Sushi" as *Restaurants* must beat the directional-prefix heuristic, whose own doc comment relies on something more specific running first. **Transfer prefix above the MCC map** because Monobank tags savings jars with the charity MCC 8398. Rungs 3 and 5 are both structured signals but no single provider supplies both, so their relative order states intent rather than breaking a live tie |
| `UNCATEGORIZED` is not a claim | The ladder treats it as a miss and continues; a full miss returns null | Preserves the backfill's null contract (row stays eligible for a pass-2 provider re-fetch) and stops a coarse rung from parking a row that a lower rung could still classify |
| `ResolveDescription` deleted | Its two callers now go through the ladder | It was a two-rung mini-ladder (keyword → transfer prefix) whose existence is precisely how the orders drifted — leaving it invites a fourth order |
| Test doubles run the REAL ladder | `StubCategoryResolver.Categorizer` wraps the stub resolver in a real `TransactionCategorizer`; the recategorization tests wrap their mocked resolver the same way | A stub categorizer would be a second copy of the order — the thing this slice exists to eliminate |
| The provider rung takes a **mapped canonical key**, never a raw wording | Callers map first: ingest via `TrueLayerCategoryMapper.Map(Classification)`, the backfill via the new `MapStored(SourceCategory)`. The join/split format lives on the mapper as `ToSourceCategory` / `MapStored` | A shared ladder still diverges if the two paths feed the same rung *different shapes*. `SourceCategory` stores TrueLayer's raw wording ("Restaurants"), which fails canonical-key validation — so the backfill silently skipped the provider rung and rung 4 re-labelled a restaurant `TRANSFER_OUT`, dropping it out of every outflow view. Caught in self-review; see the note below |

### [US2] One ordered ladder — files touched / created

- `Application/Services/CategoryMapping/ITransactionCategorizer.cs` — **new**; the port and
  `CategorizationSignals`
- `Infrastructure/Categorization/TransactionCategorizer.cs` — **new**; the ordered ladder, with
  the why-this-rung-here rationale on the method
- `Application/Services/CategoryMapping/ICategoryResolver.cs` — drop `ResolveDescription`
- `Infrastructure/Categorization/CategoryResolver.cs` — drop its implementation
- `Infrastructure/Monobank/MonobankAdapter.cs`, `Infrastructure/TrueLayer/TrueLayerAdapter.cs`,
  `Application/Services/TransactionRecategorizationService.cs` — each builds
  `CategorizationSignals` and calls the ladder; TrueLayer additionally injects
  `IActiveSubscriptionsReader` and loads the plans once per sync
- `BankSyncModule.cs` — register the categorizer as a singleton (stateless over the singleton
  resolver; the per-user plans are a call argument, never cached)
- `docs/money-semantics.md` — the ladder and its order as the money-semantics contract
- tests: `TransactionCategorizerTests` (new — order pinning), `TrueLayerAdapterTests` +
  `TransactionRecategorizationServiceTests` (ingest/backfill twins), `StubCategoryResolver`

Constraints discovered while implementing:

- `TransactionCandidate` lives in `...Application.Services`, the same namespace as
  `LoanRepaymentClassifier` — dropping the classifier's `using` from an adapter breaks the
  adapter's `IBankProvider` signatures with a confusing CS0246 on `TransactionCandidate`.
- The recategorization tests mock `ICategoryResolver` loosely, so `ResolveCanonicalKey` returned
  null and the ladder's UNCATEGORIZED check silently never fired. The fixture now sets both
  reference-table rungs to `UNCATEGORIZED` by default; Moq's last-matching-setup-wins keeps the
  per-test mappings working.
- `StubCategoryResolver.ResolveCanonicalKey` used to pass any uppercased string through, which
  would have let a raw provider string like `"Groceries > Supermarket"` short-circuit the ladder
  in tests only. It now validates against `CanonicalCategories.Definitions`, matching production.
- **Sharing the ladder is not enough on its own — the rungs must also be fed the same shapes.**
  The first cut of US2 passed `t.SourceCategory` straight into the provider rung on the backfill
  path while ingest passed `_categoryMapper.Map(t.Classification)`. `SourceCategory` is TrueLayer's
  raw wording, so canonical-key validation rejected it, the provider rung never fired on a backfill,
  and a TrueLayer restaurant described "To Go Sushi" was re-labelled `TRANSFER_OUT` — *worse* than
  the pre-US2 behaviour, which left it `UNCATEGORIZED` (still counted as spend), and unrecoverable
  because pass 2 skips rows with a non-blank `SourceCategory`. `TrueLayerAdapter` is the only
  writer of `SourceCategory`, so the round trip belongs on `TrueLayerCategoryMapper`
  (`ToSourceCategory` / `MapStored`). Pinned by
  `TransactionRecategorizationServiceTests.KeepsAProviderClassifiedRow_WhoseDescriptionLooksLikeATransfer`,
  verified to fail (`TRANSFER_OUT`) with the mapping reverted.
