namespace FinanceSentry.Modules.BankSync.Application.Services;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.BankSync.Domain.Repositories;

/// <summary>
/// Aggregated dashboard payload for a user. The service computes every <c>…Usd</c> figure in USD;
/// the controller re-expresses them in the profile base currency and stamps <see cref="BaseCurrency"/>
/// (the field names keep their historical suffix so the API shape does not change).
/// </summary>
public record DashboardData(
    Dictionary<string, decimal> AggregatedBalance,
    decimal TotalNetWorthUsd,
    int AccountCount,
    Dictionary<string, int> AccountsByType,
    IReadOnlyList<MonthlyFlow> MonthlyFlow,
    IReadOnlyList<CategoryStat> TopCategories,
    DateTime? LastSyncTimestamp,
    IReadOnlyList<MonthlyFlow>? WindowFlow = null,
    string BaseCurrency = "USD");

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
    /// <para>
    /// <paramref name="windowFrom"/> is the day-level form (1W, MTD): the window runs from that
    /// UTC calendar day, inclusive, through now. It wins over <paramref name="windowMonths"/>
    /// for the top categories and fills <see cref="DashboardData.WindowFlow"/> with the flow
    /// for exactly those days, while <see cref="DashboardData.MonthlyFlow"/> stays the
    /// whole-month history the charts plot. A date in the future clamps to today; one before
    /// the <paramref name="months"/> window's first day clamps to that day, as no earlier
    /// transactions are loaded.
    /// </para>
    /// </summary>
    Task<DashboardData> GetDashboardDataAsync(Guid userId, int months = 6, int? windowMonths = null, DateOnly? windowFrom = null, CancellationToken ct = default);
}

/// <inheritdoc />
public class DashboardQueryService(
    IAggregationService aggregation,
    IMoneyFlowStatisticsService moneyFlow,
    IMerchantCategoryStatisticsService categories,
    ICounterpartyClassificationService counterpartyClassification,
    ISyncJobRepository syncJobs,
    ICryptoHoldingsReader? cryptoHoldingsReader = null,
    IBrokerageHoldingsReader? brokerageHoldingsReader = null,
    TimeProvider? clock = null) : IDashboardQueryService
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
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
    public async Task<DashboardData> GetDashboardDataAsync(Guid userId, int months = 6, int? windowMonths = null, DateOnly? windowFrom = null, CancellationToken ct = default)
    {
        months = Math.Clamp(months, MinMonths, MaxMonths);
        var now = _clock.GetUtcNow().UtcDateTime;
        DateTime? dayFrom = windowFrom is { } day ? ClampDayWindowStart(day, months, now) : null;
        DateTime? categoriesFrom = dayFrom
            ?? (windowMonths is { } w
                ? MonthWindow.StartOfMonthsAgo(Math.Clamp(w, MinMonths, months + 1) - 1, now)
                : null);

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

        // A day-level window needs its own classification and flow: counterparty movements are
        // bucketed per month, so the whole-month result above cannot be cut at a day. The
        // whole-month flow stays as the charts' history; the windowed one backs the tiles.
        var categoryCounterparties = counterparties;
        IReadOnlyList<MonthlyFlow>? windowFlow = null;
        if (dayFrom is { } start)
        {
            categoryCounterparties = await _counterpartyClassification.ClassifyForWindowAsync(userId, months, start, ct);
            windowFlow = await _moneyFlow.GetWindowFlowAsync(userId, categoryCounterparties, months, start, ct);
        }

        // Same window as the money-flow charts unless a narrower one is asked for, so the
        // dashboard tells one story.
        var topCats = await _categories.GetTopCategoriesAsync(userId, categoryCounterparties, limit: 10, months: months, from: categoriesFrom, ct);
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
            lastSync?.CompletedAt,
            windowFlow);
    }

    // Midnight UTC of the requested day — the dashboard anchors every date in UTC — held
    // inside [first day of the loaded months window, today]: nothing earlier is fetched, and
    // a future start would be an empty window.
    private static DateTime ClampDayWindowStart(DateOnly day, int months, DateTime now)
    {
        var requested = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var today = now.Date;
        var earliest = MonthWindow.StartOfMonthsAgo(months, now);
        return new DateTime(Math.Clamp(requested.Ticks, earliest.Ticks, today.Ticks), DateTimeKind.Utc);
    }
}
