namespace FinanceSentry.Modules.BankSync.Application.Services;

using FinanceSentry.Core.Domain;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;

/// <summary>
/// Spending breakdown per merchant category for a user.
/// </summary>
public record CategoryStat(string Category, decimal TotalSpend, decimal PercentOfTotal);

/// <summary>
/// Computes the top spending categories from debit transactions.
/// </summary>
public interface IMerchantCategoryStatisticsService
{
    /// <summary>
    /// Returns the top <paramref name="limit"/> spending categories over the last
    /// <paramref name="months"/> calendar months, sorted by TotalSpend DESC.
    /// Active debit transactions are included, pending ones too — matching the
    /// money-flow convention (a hold is real spending).
    /// Outbound counterparty movement is surfaced as the FAMILY_SUPPORT bucket so that
    /// family support is visible in the category breakdown without mixing it into
    /// regular spend categories. The <paramref name="classification"/> is the same
    /// once-per-request result the money-flow reader uses, so a movement can never be
    /// spend here and a transfer there. A non-null <paramref name="from"/> narrows the window
    /// to start at that instant (month start) instead of <paramref name="months"/> complete
    /// months back, so the breakdown can match a range that begins mid-window.
    /// </summary>
    Task<IReadOnlyList<CategoryStat>> GetTopCategoriesAsync(
        Guid userId,
        CounterpartyClassificationResult classification,
        int limit = 10,
        int months = 6,
        DateTime? from = null,
        CancellationToken ct = default);
}

/// <inheritdoc />
public class MerchantCategoryStatisticsService(
    ITransactionRepository transactions,
    IBankAccountRepository accounts,
    ITransferDetectionService transferDetection) : IMerchantCategoryStatisticsService
{
    private readonly ITransactionRepository _transactions = transactions ?? throw new ArgumentNullException(nameof(transactions));
    private readonly IBankAccountRepository _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
    private readonly ITransferDetectionService _transferDetection = transferDetection ?? throw new ArgumentNullException(nameof(transferDetection));

    /// <inheritdoc />
    public async Task<IReadOnlyList<CategoryStat>> GetTopCategoriesAsync(
          Guid userId,
          CounterpartyClassificationResult classification,
          int limit = 10,
          int months = 6,
          DateTime? from = null,
          CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(classification);

        var since = from ?? MonthWindow.StartOfMonthsAgo(months);
        var sinceMonth = since.ToString("yyyy-MM");
        var txList = await _transactions.GetByUserIdSinceAsync(userId, since, ct);

        var accountList = await _accounts.GetByUserIdAsync(userId, ct);
        var currencyByAccount = accountList.ToDictionary(a => a.Id, a => a.Currency);

        // Counterparty classification (computed once upstream): matched transactions are
        // excluded from normal category stats and their outbound side appears as FAMILY_SUPPORT.
        var matchedIds = classification.MatchedTransactionIds;

        var nonCounterpartyTx = txList.Where(t => !matchedIds.Contains(t.Id)).ToList();
        var transferIds = _transferDetection.DetectTransferTransactionIds(nonCounterpartyTx, currencyByAccount);

        decimal ToUsd(Transaction t) =>
            CurrencyConverter.ToUsd(t.Amount, currencyByAccount.TryGetValue(t.AccountId, out var c) ? c : "USD");

        var debits = nonCounterpartyTx
            .Where(t => t.IsActive && t.TransactionType == "debit"
                        && !transferIds.Contains(t.Id)
                        && !CategoryKeys.IsTransfer(t.MerchantCategory))
            .ToList();

        // Aggregate outbound counterparty movement across all months in the window. It is the
        // gross amount sent — rent received back from the same person does not cancel it, since
        // that was income, not a refund. Only the family-support role is spend; investment
        // routing left the bank but not the user.
        var familySupportUsd = classification.MonthlyFlows
            .Where(f => f.FlowRole == FlowRoles.FamilySupport
                        && (from is null || string.CompareOrdinal(f.Month, sinceMonth) >= 0))
            .Sum(f => f.OutflowUsd);

        var totalSpend = debits.Sum(ToUsd) + familySupportUsd;

        if (totalSpend == 0m)
            return [];

        var result = debits
            .GroupBy(t => t.MerchantCategory ?? CategoryKeys.Uncategorized)
            .Select(g =>
            {
                var spend = g.Sum(ToUsd);
                var pct = totalSpend > 0 ? Math.Round(spend / totalSpend * 100, 2) : 0m;
                return new CategoryStat(g.Key, spend, pct);
            })
            .ToList();

        // Inject FAMILY_SUPPORT bucket when there is a net counterparty expense.
        if (familySupportUsd > 0m)
        {
            var pct = Math.Round(familySupportUsd / totalSpend * 100, 2);
            result.Add(new CategoryStat(CategoryKeys.FamilySupport, familySupportUsd, pct));
        }

        return result
            .OrderByDescending(cs => cs.TotalSpend)
            .Take(limit)
            .ToList();
    }
}
