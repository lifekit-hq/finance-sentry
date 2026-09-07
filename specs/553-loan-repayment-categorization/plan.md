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
  Pre-existing, left alone — teaching the backfill path the keyword bridge would re-categorize
  every MCC-bearing row in the book, far outside this ticket. Recorded in `docs/money-semantics.md`.
