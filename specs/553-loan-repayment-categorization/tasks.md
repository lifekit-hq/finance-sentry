# Tasks: Loan and Installment Repayments Categorized as LOAN_PAYMENTS (553)

## [US1] Loan repayments are categorized LOAN_PAYMENTS at the source

- [x] Add `GetActiveInstallmentPlansAsync` + `ActiveInstallmentPlan(Key, ExpectedAmount)` to `IActiveSubscriptionsReader` (Core), documenting why the kind filter and the amount are load-bearing
- [x] Implement it in `ActiveSubscriptionsReader` — active rows only, `Kind == SubscriptionKinds.Installment`, `MerchantNameNormalized` + `AverageAmount`
- [x] Add `LoanRepaymentClassifier.Resolve(transactionType, merchantName, description, amount, mcc, activeInstallmentPlans)` — recognizer branch, active-plan branch matching key AND amount (±15%), debit-only gate
- [x] Splice the classifier into `MonobankAdapter`'s ladder behind the keyword bridge and ahead of the transfer/MCC steps; load the plans once per `SyncTransactionsAsync` / `GetCandidatesAsync`
- [x] Consult the classifier before the MCC map in `TransactionRecategorizationService.ResolveFromRaw`; load the plans once per user
- [x] `LoanRepaymentClassifierTests`: monomarket-shaped, mortgage-shaped masked card, plain transfers (`Переказ на картку`, `Ліза ❤️`, `Поповнення «Поточний»`), credit rejection, null transaction type, empty plan set, a drift guard running the real `SubscriptionDetectionJob.DetectSubscriptions` over mortgage-shaped rows, a small transfer to a different card on the mortgage's BIN, and an amount that drifted within the plan's band
- [x] `MonobankStatementContractTests`: ladder case pinning mortgage → LOAN_PAYMENTS and `Переказ на картку` → TRANSFER_OUT through the real adapter
- [x] `TransactionRecategorizationServiceTests`: a stored MCC-4829 `TRANSFER_OUT` mortgage row is re-resolved to LOAN_PAYMENTS without a provider call
- [x] `ActiveSubscriptionsReaderTests`: installment-kind only, subscription-kind excluded, expected amount carried through
- [x] `MoneyFlowStatisticsTests`: mortgage-shaped and monomarket-shaped debits land in outflow and in committed
- [x] Update `docs/money-semantics.md` with the repayment carve-out from the transfer exclusion
- [x] `dotnet build backend/FinanceSentry.sln -c Release -m:1` — zero warnings
- [x] `dotnet test backend/FinanceSentry.sln --no-build -c Release -m:1 --filter "Category!=Integration"` — green
- [x] Commit spec artifacts + code

## [US2] One ordered ladder decides every category, on every path

- [x] Add `ITransactionCategorizer` + `CategorizationSignals` (Application/Services/CategoryMapping), documenting what each signal is and why null ≠ `UNCATEGORIZED`
- [x] Implement `TransactionCategorizer` (Infrastructure/Categorization) — keyword → loan → provider category → transfer description → MCC, with the rationale for each rung's rank
- [x] Register it in `BankSyncModule` as a singleton, noting why the per-user plans stay a call argument
- [x] `MonobankAdapter` — build `CategorizationSignals` and call the ladder instead of hand-rolling its own order
- [x] `TrueLayerAdapter` — same, plus inject `IActiveSubscriptionsReader` and load the installment plans once per sync (this path never consulted the loan rule at all)
- [x] `TransactionRecategorizationService.ResolveFromRaw` — collapse to a single ladder call, preserving the null-means-eligible-for-re-fetch contract
- [x] Delete `ICategoryResolver.ResolveDescription` and its implementation — the two-rung mini-ladder that let the orders drift
- [x] `StubCategoryResolver` — expose the REAL ladder over the stub's reference data, and validate canonical keys against `CanonicalCategories` as production does
- [x] `TransactionCategorizerTests` — pin every rung against the rung below it: keyword > MCC, keyword > provider category, loan > MCC 4829, loan > a provider "transfer" classification, loan reaches the mortgage via an active plan, credits unclaimed, provider category > transfer prefix, a non-canonical provider string falls through, transfer prefix > charity MCC 8398, genuine card-to-card stays `TRANSFER_OUT`, MCC as the floor, and no-signal → null
- [x] `TrueLayerAdapterTests` — an installment-worded TrueLayer debit ingests as `LOAN_PAYMENTS`
- [x] `TransactionRecategorizationServiceTests` — its twin (same row, same answer) plus a keyword-beats-MCC backfill case
- [x] Update `docs/money-semantics.md` — the ladder and its order as the contract, replacing the recorded ingest/backfill divergence
- [x] Feed the provider rung the same SHAPE on every path: `TrueLayerCategoryMapper.ToSourceCategory` / `MapStored` round trip, the backfill maps the stored `SourceCategory` before the ladder (self-review found a raw string skipping the rung and re-labelling TrueLayer restaurants `TRANSFER_OUT`)
- [x] `TrueLayerCategoryMapperTests` — the stored round trip agrees with live mapping, multi-segment paths keep every segment, unmappable wordings return `UNCATEGORIZED`
- [x] `TransactionRecategorizationServiceTests.KeepsAProviderClassifiedRow_WhoseDescriptionLooksLikeATransfer` — verified to fail with the mapping reverted
- [x] `TransactionCategorizerTests` — add the missing rung-1-beats-rung-2 fixture (keyword bridge over the loan rule)
- [x] `dotnet build backend/FinanceSentry.sln -c Release -m:1` — zero warnings
- [x] `dotnet test backend/FinanceSentry.sln --no-build -c Release -m:1 --filter "Category!=Integration"` — green
- [x] Commit spec artifacts + code
