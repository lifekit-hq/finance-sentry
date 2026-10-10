namespace FinanceSentry.Modules.BankSync.Application.Services;

using FinanceSentry.Core.Domain;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;

/// <summary>
/// The bucket a transaction landed in for the month's flow figures. These are the audit
/// labels behind the dashboard tiles: summing <see cref="FlowBreakdownItem.AmountUsd"/> over
/// a bucket reproduces the tile — income over <see cref="Income"/>, outflow over
/// <see cref="Spending"/>, invested over <see cref="Invested"/> — and the excluded buckets
/// show what the math deliberately ignored.
/// </summary>
public static class FlowBuckets
{
    /// <summary>Counts into inflow: a normal credit, or a non-investment counterparty credit.</summary>
    public const string Income = "income";

    /// <summary>Counts into outflow: a normal debit, or a non-investment counterparty debit.</summary>
    public const string Spending = "spending";

    /// <summary>Investment-routing debit: money changing sleeve, reported as invested, never outflow.</summary>
    public const string Invested = "invested";

    /// <summary>Investment-routing credit: capital coming back, not income.</summary>
    public const string InvestmentReturn = "investment-return";

    /// <summary>One leg of a detected debit↔credit pair between the user's own accounts.</summary>
    public const string ExcludedPair = "excluded-pair";

    /// <summary>A self-routing counterparty leg: the user's own money mid-hop, both directions excluded.</summary>
    public const string ExcludedRouting = "excluded-routing";

    /// <summary>Carries a TRANSFER_IN/TRANSFER_OUT category and matched nothing else.</summary>
    public const string ExcludedTransfer = "excluded-transfer";
}

/// <summary>One transaction with the classification the flow statistics applied to it.</summary>
public record FlowBreakdownItem(
    Guid TransactionId,
    Guid AccountId,
    string BankName,
    string AccountLast4,
    string Currency,
    decimal Amount,
    decimal AmountUsd,
    DateTime Date,
    string Description,
    string? MerchantName,
    string? Category,
    string Direction,
    string Bucket,
    string? CounterpartyName,
    string? FlowRole);

/// <summary>
/// A month's — or, for the dashboard's day-level windows, a date range's — transactions, each
/// labelled with the bucket it landed in. <see cref="Month"/> is empty for a range, which
/// echoes its UTC calendar days in <see cref="From"/> / <see cref="To"/> instead.
/// </summary>
public record FlowBreakdown(
    string Month,
    IReadOnlyList<FlowBreakdownItem> Items,
    DateOnly? From = null,
    DateOnly? To = null);

/// <summary>
/// Explains a month of the money-flow statistics transaction by transaction.
/// </summary>
public interface IFlowBreakdownService
{
    /// <summary>
    /// Returns every credit/debit of <paramref name="month"/> labelled with the bucket the
    /// flow statistics put it in. <paramref name="months"/> is the statistics window the
    /// dashboard was rendered with: classification and transfer-pair detection run over that
    /// whole window (a pair can straddle a month boundary), exactly as
    /// <see cref="IMoneyFlowStatisticsService"/> runs them, and the result is then filtered
    /// to the requested month — so the labels here are the tiles' own arithmetic, not a
    /// reimplementation that can drift.
    /// </summary>
    Task<FlowBreakdown> GetBreakdownAsync(
        Guid userId,
        string month,
        int months = 6,
        CancellationToken ct = default);

    /// <summary>
    /// The day-level form of <see cref="GetBreakdownAsync"/>: every credit/debit dated from
    /// <paramref name="from"/> through <paramref name="to"/> (UTC calendar days, both
    /// inclusive), labelled exactly as the dashboard's windowed tiles labelled them. The
    /// classification and pair detection still run over the whole
    /// <paramref name="months"/> window, so a pair straddling the range's edge is paired the
    /// same way. The range is held inside that window's loaded days.
    /// </summary>
    Task<FlowBreakdown> GetRangeBreakdownAsync(
        Guid userId,
        DateOnly from,
        DateOnly to,
        int months = 6,
        CancellationToken ct = default);
}

/// <inheritdoc />
public class FlowBreakdownService(
    ITransactionRepository transactions,
    IBankAccountRepository accounts,
    ITransferDetectionService transferDetection,
    ICounterpartyClassificationService counterpartyClassification) : IFlowBreakdownService
{
    private readonly ITransactionRepository _transactions = transactions ?? throw new ArgumentNullException(nameof(transactions));
    private readonly IBankAccountRepository _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
    private readonly ITransferDetectionService _transferDetection = transferDetection ?? throw new ArgumentNullException(nameof(transferDetection));
    private readonly ICounterpartyClassificationService _counterpartyClassification = counterpartyClassification ?? throw new ArgumentNullException(nameof(counterpartyClassification));

    private const string CreditType = "credit";
    private const string DebitType = "debit";

    /// <inheritdoc />
    public async Task<FlowBreakdown> GetBreakdownAsync(
        Guid userId,
        string month,
        int months = 6,
        CancellationToken ct = default)
    {
        var items = await BuildItemsAsync(
            userId, months, d => d.ToString("yyyy-MM") == month, ct);
        return new FlowBreakdown(month, items);
    }

    /// <inheritdoc />
    public async Task<FlowBreakdown> GetRangeBreakdownAsync(
        Guid userId,
        DateOnly from,
        DateOnly to,
        int months = 6,
        CancellationToken ct = default)
    {
        var start = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        // `to` is a whole day: everything before the next midnight.
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var items = await BuildItemsAsync(userId, months, d => d >= start && d < end, ct);
        return new FlowBreakdown(string.Empty, items, from, to);
    }

    private async Task<List<FlowBreakdownItem>> BuildItemsAsync(
        Guid userId,
        int months,
        Func<DateTime, bool> inRange,
        CancellationToken ct)
    {
        // Mirrors MoneyFlowStatisticsService step for step: same window, same account-currency
        // map, same shared classification entry point (memoized per request), same transfer
        // detection over the non-counterparty remainder.
        var accountList = await _accounts.GetByUserIdAsync(userId, ct);
        var accountsById = accountList.ToDictionary(a => a.Id);
        var accountCurrencies = accountsById.ToDictionary(kv => kv.Key, kv => kv.Value.Currency);

        var txList = (await _transactions.GetByUserIdSinceAsync(
            userId, MonthWindow.StartOfMonthsAgo(months), ct)).ToList();

        var classification = await _counterpartyClassification.ClassifyForWindowAsync(userId, months, ct);
        var matches = classification.Matches ?? new Dictionary<Guid, CounterpartyMatch>();
        var matchedIds = classification.MatchedTransactionIds;

        var nonCounterpartyTx = txList.Where(t => !matchedIds.Contains(t.Id)).ToList();
        var transferIds = _transferDetection.DetectTransferTransactionIds(nonCounterpartyTx, accountCurrencies);

        var items = txList
            .Where(t => t.IsActive
                        && (t.TransactionType == CreditType || t.TransactionType == DebitType)
                        && inRange(t.PostedDate ?? t.TransactionDate))
            .Select(t =>
            {
                var isCredit = t.TransactionType == CreditType;
                var (bucket, counterparty) = Classify(t, isCredit, matches, transferIds);
                var currency = accountCurrencies.TryGetValue(t.AccountId, out var cur) ? cur : "UNKNOWN";
                accountsById.TryGetValue(t.AccountId, out var account);

                return new FlowBreakdownItem(
                    t.Id,
                    t.AccountId,
                    account?.BankName ?? "Unknown",
                    account?.AccountNumberLast4 ?? "",
                    currency,
                    t.Amount,
                    CurrencyConverter.ToUsd(t.Amount, currency),
                    t.PostedDate ?? t.TransactionDate,
                    t.Description,
                    t.MerchantName,
                    t.MerchantCategory,
                    isCredit ? "in" : "out",
                    bucket,
                    counterparty?.Name,
                    counterparty?.FlowRole);
            })
            .OrderByDescending(i => i.Date)
            .ToList();

        return items;
    }

    private static (string Bucket, CounterpartyMatch? Counterparty) Classify(
        Transaction t,
        bool isCredit,
        IReadOnlyDictionary<Guid, CounterpartyMatch> matches,
        HashSet<Guid> transferIds)
    {
        if (matches.TryGetValue(t.Id, out var counterparty))
        {
            var bucket = counterparty.FlowRole switch
            {
                FlowRoles.Investment => isCredit ? FlowBuckets.InvestmentReturn : FlowBuckets.Invested,
                FlowRoles.SelfRouting => FlowBuckets.ExcludedRouting,
                _ => isCredit ? FlowBuckets.Income : FlowBuckets.Spending,
            };
            return (bucket, counterparty);
        }

        if (transferIds.Contains(t.Id))
            return (FlowBuckets.ExcludedPair, null);

        if (CategoryKeys.IsTransfer(t.MerchantCategory))
            return (FlowBuckets.ExcludedTransfer, null);

        return (isCredit ? FlowBuckets.Income : FlowBuckets.Spending, null);
    }
}
