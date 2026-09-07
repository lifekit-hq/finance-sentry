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
