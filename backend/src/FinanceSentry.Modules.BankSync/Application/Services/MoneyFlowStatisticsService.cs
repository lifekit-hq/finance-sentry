namespace FinanceSentry.Modules.BankSync.Application.Services;

using FinanceSentry.Core.Domain;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;

/// <summary>
/// Monthly cash-flow statistics for a user (inflow / outflow / net per currency).
/// Amounts are provided both in the source currency and normalized to USD so
/// consumers can render a single cross-currency total per month.
/// <para>
/// <see cref="OutflowUsd"/> is partitioned into <see cref="CommittedOutflowUsd"/> and
/// <see cref="DiscretionaryOutflowUsd"/>, which always sum back to it. Only the USD figures
/// are split: the split exists to be aggregated across currencies, and a native
/// committed total has no meaning once rows from a UAH and a EUR account sit side by side.
/// </para>
/// <para>
/// <see cref="FamilySupportOutflowUsd"/> and <see cref="InvestedOutflowUsd"/> carry the
/// counterparty breakdown (spec 044). They are only ever non-zero on the synthetic USD row
/// that holds a month's counterparty adjustment — see <see cref="IMoneyFlowStatisticsService"/>.
/// </para>
/// </summary>
public record MonthlyFlow(
    string Month,       // "2026-03"
    string Currency,
    decimal Inflow,
    decimal Outflow,
    decimal Net,
    decimal InflowUsd,
    decimal OutflowUsd,
    decimal NetUsd,
    decimal CommittedOutflowUsd,
    decimal DiscretionaryOutflowUsd,
    decimal FamilySupportOutflowUsd = 0m,
    decimal InvestedOutflowUsd = 0m);

/// <summary>
/// Computes monthly money-flow statistics using an in-memory join of transactions and accounts.
/// </summary>
public interface IMoneyFlowStatisticsService
{
    /// <summary>
    /// Returns monthly inflow/outflow/net per currency over the last <paramref name="months"/>
    /// COMPLETE calendar months plus the one in progress. The window starts on a month
    /// boundary (see <see cref="MonthWindow"/>) so no bucket is a partial fragment of a month;
    /// the trailing in-progress bucket is included so callers can render a month-to-date
    /// figure, and it is up to them to keep it out of month-over-month comparisons.
    /// Pending transactions count — a card hold is real spending, and excluding it made the
    /// current month's outflow a fraction of reality. Settlement retires or flips the pending
    /// row in place, so it is never double-counted for long.
    /// <para>
    /// <b>Committed vs discretionary match rule.</b> An outflow is COMMITTED when the key
    /// derived from it by <see cref="CommitmentKeyResolver.Resolve"/> is the key of one of the
    /// user's detected commitments whose status is <c>active</c> — the same key the detector
    /// grouped the charge under, so the two sides cannot drift apart. That covers both kinds of
    /// commitment the detector stores: recurring services, keyed by normalized merchant name,
    /// and installment (розстрочка) plans, keyed as
    /// <c>installment:{merchant}:{roundedAmount}</c>. Every other non-transfer outflow is
    /// DISCRETIONARY. Transfers are excluded from both, exactly as they are from
    /// <c>Outflow</c> — but a repayment to a masked card is no longer one of them: since #553
    /// <see cref="LoanRepaymentClassifier"/> categorizes it <c>LOAN_PAYMENTS</c> at the source,
    /// so the mortgage now lands in outflow and, via its plan key, in committed.
    /// </para>
    /// <para>
    /// <b>Counterparty flows.</b> Counterparty transactions (e.g. family rent / support) are
    /// classified per direction — gross, never netted against each other — and emitted as one
    /// synthetic USD row per month (native amounts zero; the classification is already
    /// currency-normalised), so per-currency rows never mix native sums with a USD adjustment.
    /// The <paramref name="classification"/> is computed once per request by
    /// <see cref="ICounterpartyClassificationService.ClassifyForWindowAsync"/> and shared with
    /// the top-categories reader, so both tell the same story about the same movements.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<MonthlyFlow>> GetMonthlyFlowAsync(
        Guid userId,
        CounterpartyClassificationResult classification,
        int months = 6,
        CancellationToken ct = default);
}

/// <inheritdoc />
public class MoneyFlowStatisticsService(
    ITransactionRepository transactions,
    IBankAccountRepository accounts,
    ITransferDetectionService transferDetection,
    IActiveSubscriptionsReader activeSubscriptions) : IMoneyFlowStatisticsService
{
    private readonly ITransactionRepository _transactions = transactions ?? throw new ArgumentNullException(nameof(transactions));
    private readonly IBankAccountRepository _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
    private readonly ITransferDetectionService _transferDetection = transferDetection ?? throw new ArgumentNullException(nameof(transferDetection));
    private readonly IActiveSubscriptionsReader _activeSubscriptions = activeSubscriptions ?? throw new ArgumentNullException(nameof(activeSubscriptions));

    /// <inheritdoc />
    public async Task<IReadOnlyList<MonthlyFlow>> GetMonthlyFlowAsync(
          Guid userId,
          CounterpartyClassificationResult classification,
          int months = 6,
          CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(classification);

        // Floor to the first day of the month so the oldest bucket is a WHOLE month.
        var since = MonthWindow.StartOfMonthsAgo(months);

        // 1. Build currency map from active accounts
        var accountList = await _accounts.GetByUserIdAsync(userId, ct);
        var accountCurrencies = accountList
            .Where(a => a.IsActive)
            .ToDictionary(a => a.Id, a => a.Currency);

        // 2. Fetch transactions in window
        var txList = (await _transactions.GetByUserIdSinceAsync(userId, since, ct)).ToList();

        // 3. Counterparty classification (computed once upstream): identifies transactions that
        //    belong to known counterparties (e.g. family rent / support) and carries their gross
        //    per-direction monthly flows. These are excluded from the normal transfer-detection
        //    pass to avoid double-exclusion.
        var matchedIds = classification.MatchedTransactionIds;

        // 4. Detect internal transfer pairs among NON-counterparty transactions.
        var nonCounterpartyTx = txList.Where(t => !matchedIds.Contains(t.Id)).ToList();
        var transferIds = _transferDetection.DetectTransferTransactionIds(nonCounterpartyTx, accountCurrencies);

        // 5. Merchant keys of the user's active commitments — the committed/discretionary
        // classifier. Read once for the whole window; the detector's status is point-in-time,
        // so a subscription cancelled today reclassifies its past charges too.
        var committedMerchantKeys = await _activeSubscriptions.GetActiveCommitmentMerchantKeysAsync(userId, ct);

        // 6. Normal flow: exclude counterparty-matched, transfer pairs, and TRANSFER category,
        // then group by (currency, year-month) and sum inflow/outflow (pending included —
        // a hold is committed money; see interface doc).
        var result = txList
            .Where(t => t.IsActive
                        && !matchedIds.Contains(t.Id)
                        && !transferIds.Contains(t.Id)
                        && !CategoryKeys.IsTransfer(t.MerchantCategory))
            .Select(t => new
            {
                Transaction = t,
                Currency = accountCurrencies.TryGetValue(t.AccountId, out var cur) ? cur : "UNKNOWN",
                EffectiveDate = t.PostedDate ?? t.TransactionDate
            })
            .GroupBy(x => new { x.Currency, Month = x.EffectiveDate.ToString("yyyy-MM") })
            .Select(g =>
            {
                var debits = g.Where(x => x.Transaction.TransactionType == "debit").ToList();
                var inflow = g.Where(x => x.Transaction.TransactionType == "credit").Sum(x => x.Transaction.Amount);
                var outflow = debits.Sum(x => x.Transaction.Amount);
                var committed = debits
                    .Where(x => committedMerchantKeys.Contains(
                        CommitmentKeyResolver.Resolve(
                            x.Transaction.MerchantName, x.Transaction.Description,
                            x.Transaction.Amount, x.Transaction.Mcc)))
                    .Sum(x => x.Transaction.Amount);

                var inflowUsd = CurrencyConverter.ToUsd(inflow, g.Key.Currency);
                var outflowUsd = CurrencyConverter.ToUsd(outflow, g.Key.Currency);
                // Convert committed only, then subtract: converting both subsets independently
                // lets rounding split them apart from OutflowUsd.
                var committedUsd = CurrencyConverter.ToUsd(committed, g.Key.Currency);

                return new MonthlyFlow(
                    g.Key.Month,
                    g.Key.Currency,
                    inflow,
                    outflow,
                    inflow - outflow,
                    inflowUsd,
                    outflowUsd,
                    inflowUsd - outflowUsd,
                    committedUsd,
                    outflowUsd - committedUsd);
            })
            .ToList();

        // 7. Emit each month's counterparty flows as ONE synthetic USD row per month, native
        //    amounts zero (the classification is already currency-normalised). It never rides
        //    on a per-currency bucket: which bucket "comes first" is dictionary-iteration
        //    order, and folding USD adjustments into a UAH row's USD figures made that row's
        //    native and USD columns tell different stories.
        //
        //    Each direction lands on its own, ungrossed: rent arriving from a counterparty is
        //    income for its full amount even in a month when support went back out to the same
        //    counterparty, and that support is spending for its full amount. Netting the pair
        //    reported neither, which is exactly the transfer-blind savings rate this reads
        //    against.
        //
        //    The flow ROLE decides where each direction lands:
        //      family_support — real spending, so outbound joins Outflow, inbound joins Inflow;
        //      investment     — the money is still the user's, it only changed sleeve. Outbound
        //                       stays OUT of Outflow and is reported separately; inbound is
        //                       capital coming back, not income, so it is not Inflow either.
        //                       Folding either into spend/income would misstate the savings rate
        //                       by exactly the amount that was saved.
        //
        //    Counterparty spend has no commitment merchant key, so within the synthetic row the
        //    whole outflow lands as discretionary — keeping the committed + discretionary
        //    partition of OutflowUsd exact across every row.
        var counterpartyRows = classification.MonthlyFlows
            .GroupBy(f => f.Month)
            .Select(g =>
            {
                // self_routing is the user's own money mid-hop: like investment it stays out
                // of income and spending, but unlike investment it is reported nowhere at all.
                var realFlows = g.Where(f =>
                    f.FlowRole != FlowRoles.Investment && f.FlowRole != FlowRoles.SelfRouting).ToList();
                var incomeUsd = realFlows.Sum(f => f.InflowUsd);
                var expenseUsd = realFlows.Sum(f => f.OutflowUsd);
                var familySupportUsd = g.Where(f => f.FlowRole == FlowRoles.FamilySupport).Sum(f => f.OutflowUsd);
                var investedUsd = g.Where(f => f.FlowRole == FlowRoles.Investment).Sum(f => f.OutflowUsd);

                return new MonthlyFlow(
                    g.Key, "USD",
                    0m, 0m, 0m,
                    incomeUsd, expenseUsd, incomeUsd - expenseUsd,
                    CommittedOutflowUsd: 0m,
                    DiscretionaryOutflowUsd: expenseUsd,
                    familySupportUsd, investedUsd);
            });

        result.AddRange(counterpartyRows);

        return result
            .OrderByDescending(mf => mf.Month)
            .ToList();
    }
}
