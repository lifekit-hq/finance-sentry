namespace FinanceSentry.Modules.BankSync.Application.Services;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.BankSync.Domain.Repositories;

/// <summary>
/// Aggregated dashboard payload for a user.
/// </summary>
public record DashboardData(
    Dictionary<string, decimal> AggregatedBalance,
    decimal TotalNetWorthUsd,
    int AccountCount,
    Dictionary<string, int> AccountsByType,
    IReadOnlyList<MonthlyFlow> MonthlyFlow,
    IReadOnlyList<CategoryStat> TopCategories,
    DateTime? LastSyncTimestamp);

/// <summary>
/// Composes all dashboard data in a single call.
/// </summary>
public interface IDashboardQueryService
{
    /// <summary>
    /// Returns the full dashboard payload for the given user. <paramref name="months"/> sets
    /// the window for the month-bucketed statistics (money flow, top categories) so every
    /// dashboard widget tells the same time-range story; point-in-time figures (net worth,
    /// balances) are unaffected. <paramref name="windowMonths"/>, when given, is the number of
    /// calendar months (in-progress one included) the top categories cover, so they can match
    /// a range narrower than the <paramref name="months"/> history the charts plot.
    /// </summary>
    Task<DashboardData> GetDashboardDataAsync(Guid userId, int months = 6, int? windowMonths = null, CancellationToken ct = default);
}

/// <inheritdoc />
public class DashboardQueryService(
    IAggregationService aggregation,
    IMoneyFlowStatisticsService moneyFlow,
    IMerchantCategoryStatisticsService categories,
    ICounterpartyClassificationService counterpartyClassification,
    ISyncJobRepository syncJobs,
    ICryptoHoldingsReader? cryptoHoldingsReader = null,
    IBrokerageHoldingsReader? brokerageHoldingsReader = null) : IDashboardQueryService
{
    private readonly IAggregationService _aggregation = aggregation ?? throw new ArgumentNullException(nameof(aggregation));
    private readonly IMoneyFlowStatisticsService _moneyFlow = moneyFlow ?? throw new ArgumentNullException(nameof(moneyFlow));
    private readonly IMerchantCategoryStatisticsService _categories = categories ?? throw new ArgumentNullException(nameof(categories));
    private readonly ICounterpartyClassificationService _counterpartyClassification = counterpartyClassification ?? throw new ArgumentNullException(nameof(counterpartyClassification));
    private readonly ISyncJobRepository _syncJobs = syncJobs ?? throw new ArgumentNullException(nameof(syncJobs));
    private readonly ICryptoHoldingsReader? _cryptoHoldingsReader = cryptoHoldingsReader;
    private readonly IBrokerageHoldingsReader? _brokerageHoldingsReader = brokerageHoldingsReader;

    private const int MinMonths = 1;
    private const int MaxMonths = 120;

    /// <inheritdoc />
    public async Task<DashboardData> GetDashboardDataAsync(Guid userId, int months = 6, int? windowMonths = null, CancellationToken ct = default)
    {
        months = Math.Clamp(months, MinMonths, MaxMonths);
        DateTime? categoriesFrom = windowMonths is { } w
            ? MonthWindow.StartOfMonthsAgo(Math.Clamp(w, MinMonths, months + 1) - 1)
            : null;

        // Sequential — DbContext is scoped per request and not thread-safe.
        // Fan-out would require IDbContextFactory.
        var balance = await _aggregation.GetAggregatedBalanceAsync(userId, ct);
        var bankTotalUsd = await _aggregation.GetTotalNetWorthUsdAsync(userId, ct);
        var byType = await _aggregation.GetAccountCountByTypeAsync(userId, ct);
        // Counterparty classification runs ONCE and is handed to both readers. Cash flow (and
        // therefore the savings rate) and top categories must agree on which movements were
        // family support, which were investment routing, and which stayed transfers — classifying
        // twice invites two answers for one month.
        var counterparties = await _counterpartyClassification.ClassifyForWindowAsync(userId, months, ct);
        var flow = await _moneyFlow.GetMonthlyFlowAsync(userId, counterparties, months, ct);
        // Same window as the money-flow charts unless a narrower one is asked for, so the
        // dashboard tells one story.
        var topCats = await _categories.GetTopCategoriesAsync(userId, counterparties, limit: 10, months: months, from: categoriesFrom, ct);
        var lastSync = await _syncJobs.GetLatestSuccessfulByUserIdAsync(userId, ct);

        var cryptoHoldings = _cryptoHoldingsReader is not null
            ? await _cryptoHoldingsReader.GetHoldingsAsync(userId, ct)
            : [];
        var brokerageHoldings = _brokerageHoldingsReader is not null
            ? await _brokerageHoldingsReader.GetHoldingsAsync(userId, ct)
            : [];

        var cryptoTotalUsd = cryptoHoldings.Sum(h => h.UsdValue);
        var brokerageTotalUsd = brokerageHoldings.Sum(h => h.UsdValue);

        var cryptoConnections = cryptoHoldings.Select(h => h.Provider).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var brokerageConnections = brokerageHoldings.Select(h => h.Provider).Distinct(StringComparer.OrdinalIgnoreCase).Count();

        if (cryptoConnections > 0)
        {
            byType["crypto"] = cryptoConnections;
        }
        if (brokerageConnections > 0)
        {
            byType["brokerage"] = brokerageConnections;
        }

        var nonBankUsd = cryptoTotalUsd + brokerageTotalUsd;
        if (nonBankUsd > 0)
        {
            balance["USD"] = balance.TryGetValue("USD", out var existingUsd) ? existingUsd + nonBankUsd : nonBankUsd;
        }

        var accountCount = byType.Values.Sum();

        return new DashboardData(
            balance,
            bankTotalUsd + cryptoTotalUsd + brokerageTotalUsd,
            accountCount,
            byType,
            flow,
            topCats,
            lastSync?.CompletedAt);
    }
}
