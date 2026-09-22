namespace FinanceSentry.Modules.Budgets.Infrastructure.Jobs;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.Budgets.Application.Services;
using FinanceSentry.Modules.Budgets.Domain;
using FinanceSentry.Modules.Budgets.Domain.Repositories;
using Microsoft.Extensions.Logging;

/// <summary>
/// Daily hygiene sentinel (C2, ledger-heartbeat design): fires a BudgetBreach alert when a monthly
/// budget's month-to-date spend reaches 90% or 100% of its limit. Reads budgets.budgets
/// (<see cref="IBudgetRepository"/>) and month-to-date category spend, already converted to USD at
/// the read boundary (<see cref="IMerchantSpendingReader"/> — never a native amount is summed). The
/// budget's own limit is converted the same way via <see cref="CurrencyConverter.ToUsd"/>, mirroring
/// <c>GetBudgetSummaryQueryHandler</c> so the two figures are compared apples-to-apples.
///
/// Both thresholds are checked independently every run, not as an if/else-if ladder: a budget that
/// jumps straight from 70% to 105% spend in one day crosses both the same day, and each is its own
/// alert. Dedup — the part that makes a crossing fire once per budget per month — lives entirely in
/// <see cref="IAlertGeneratorService"/> (its reference id carries the budget, the crossing kind, and
/// the year/month); this job just reports what it observed on every run.
///
/// Month-end grace: for the first <see cref="PreviousMonthGraceDays"/> days of a month each run
/// also re-evaluates the PREVIOUS month, so spend posted on its last day (after the final run that
/// covered it) and bank transactions that post late are still seen. Seven days matches the bank
/// adapters' resync overlap (ResyncLookbackDays in the Monobank and TrueLayer adapters) — the
/// window in which a transaction dated last month can still arrive. The month-scoped reference id
/// keeps both periods' alerts apart, so no second dedup mechanism is needed.
///
/// Two decisions worth stating (each pinned by its own test):
/// - A mid-month limit edit is never snapshotted — every run reads the budget's CURRENT
///   MonthlyLimit and recomputes against it. An edit changes what counts as a crossing going
///   forward; it never retroactively resolves an alert already raised earlier in the month.
/// - A refund that drops spend back under an already-alerted threshold does not resolve that
///   alert — BudgetBreach has no Resolve method, like the other hygiene sentinels (PriceHike,
///   CategorySpike, …). If spend later climbs back over the same threshold in the same month, the
///   still-active alert on that (budget, kind, month) reference suppresses a second one.
/// </summary>
public sealed class BudgetBreachDetectionJob(
    IBudgetRepository budgets,
    IMerchantSpendingReader merchantSpending,
    ICategoryNormalizationService normalization,
    IAlertGeneratorService alerts,
    TimeProvider clock,
    ILogger<BudgetBreachDetectionJob> logger)
{
    private const decimal NearLimitThreshold = 0.90m;
    private const decimal ExceededThreshold = 1.0m;
    private const int PreviousMonthGraceDays = 7;

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var currentMonth = new DateOnly(today.Year, today.Month, 1);
        List<DateOnly> months = today.Day <= PreviousMonthGraceDays
            ? [currentMonth.AddMonths(-1), currentMonth]
            : [currentMonth];

        IReadOnlyList<Budget> all;
        try
        {
            all = await budgets.GetAllAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "BudgetBreachDetectionJob: failed to read budgets");
            return;
        }

        foreach (var group in all.GroupBy(b => b.UserId))
        {
            foreach (var monthStart in months)
            {
                await ProcessUserAsync(group.Key, group.ToList(), monthStart, ct);
            }
        }
    }

    private async Task ProcessUserAsync(
        Guid userId, IReadOnlyList<Budget> userBudgets, DateOnly monthStart, CancellationToken ct)
    {
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        IReadOnlyDictionary<string, decimal> rawSpending;
        try
        {
            rawSpending = await merchantSpending.GetSpendingByCategoryUsdAsync(userId, monthStart, monthEnd, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "BudgetBreachDetectionJob: failed to read spend for user {UserId}", userId);
            return;
        }

        var spentByCategory = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rawCategory, amount) in rawSpending)
        {
            var normalized = normalization.Normalize(rawCategory);
            spentByCategory[normalized] = spentByCategory.GetValueOrDefault(normalized) + amount;
        }

        foreach (var budget in userBudgets)
        {
            try
            {
                await EvaluateAsync(budget, spentByCategory, monthStart.Year, monthStart.Month, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "BudgetBreachDetectionJob: alert failed for user {UserId} category {Category}",
                    userId, budget.Category);
            }
        }
    }

    private async Task EvaluateAsync(
        Budget budget, IReadOnlyDictionary<string, decimal> spentByCategory, int year, int month,
        CancellationToken ct)
    {
        var limitUsd = CurrencyConverter.ToUsd(budget.MonthlyLimit, budget.Currency);
        if (limitUsd <= 0m)
        {
            return;
        }

        var spentUsd = spentByCategory.GetValueOrDefault(budget.Category, 0m);
        var ratio = spentUsd / limitUsd;

        if (ratio >= NearLimitThreshold)
        {
            await alerts.GenerateBudgetNearLimitAlertAsync(
                budget.UserId, budget.Id, budget.Category, spentUsd, limitUsd, year, month, ct);
        }

        if (ratio >= ExceededThreshold)
        {
            await alerts.GenerateBudgetExceededAlertAsync(
                budget.UserId, budget.Id, budget.Category, spentUsd, limitUsd, year, month, ct);
        }
    }
}
