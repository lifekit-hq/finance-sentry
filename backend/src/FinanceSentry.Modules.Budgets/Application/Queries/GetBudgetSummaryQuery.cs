namespace FinanceSentry.Modules.Budgets.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.Budgets.API.Responses;
using FinanceSentry.Modules.Budgets.Application.Services;
using FinanceSentry.Modules.Budgets.Domain.Exceptions;
using FinanceSentry.Modules.Budgets.Domain.Repositories;

public record GetBudgetSummaryQuery(Guid UserId, int? Year = null, int? Month = null)
    : IQuery<BudgetSummaryResponse>;

public class GetBudgetSummaryQueryHandler(
    IBudgetRepository budgets,
    ICategoryNormalizationService normalization,
    IMerchantSpendingReader merchantSpending,
    TimeProvider? clock = null)
    : IQueryHandler<GetBudgetSummaryQuery, BudgetSummaryResponse>
{
    private readonly IBudgetRepository _budgets = budgets;
    private readonly ICategoryNormalizationService _normalization = normalization;
    private readonly IMerchantSpendingReader _merchantSpending = merchantSpending;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<BudgetSummaryResponse> Handle(GetBudgetSummaryQuery request, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        var year = request.Year ?? now.Year;
        var month = request.Month ?? now.Month;

        if (month < 1 || month > 12 || year < 2020)
            throw new BudgetInvalidPeriodException();

        var from = new DateOnly(year, month, 1);
        var to = from.AddMonths(1).AddDays(-1);

        // A month still in progress is read as of today; a past (or future) month reads as
        // complete, so its pace ratio collapses to the plain spent / limit ratio.
        var isCurrentMonth = year == now.Year && month == now.Month;
        var asOf = isCurrentMonth ? now.UtcDateTime.Date : new DateTime(year, month, DateTime.DaysInMonth(year, month));

        var userBudgets = await _budgets.GetByUserIdAsync(request.UserId, cancellationToken);

        var rawSpending = await _merchantSpending.GetSpendingByCategoryUsdAsync(
            request.UserId, from, to, cancellationToken);

        var spentByCategory = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rawCategory, amount) in rawSpending)
        {
            var normalized = _normalization.Normalize(rawCategory);
            spentByCategory[normalized] = spentByCategory.GetValueOrDefault(normalized) + amount;
        }

        // Spend arrives already in USD (GetMerchantSpendingQuery converts). Normalize each
        // budget's limit to USD too so the comparison, remaining, and totals are apples-to-apples
        // across budgets defined in different currencies. The summary is a single-currency (USD) view.
        var items = userBudgets.Select(b =>
        {
            var limitUsd = CurrencyConverter.ToUsd(b.MonthlyLimit, b.Currency);
            var spent = spentByCategory.GetValueOrDefault(b.Category, 0m);
            var remaining = limitUsd - spent;
            var pace = BudgetPace.Calculate(spent, limitUsd, asOf);
            var isOverBudget = spent > limitUsd;
            // Already over the limit is the louder signal; pace is for "heading there".
            var isOffPace = isCurrentMonth
                && !isOverBudget
                && asOf.Day >= BudgetPace.WindowStartDay
                && asOf.Day <= BudgetPace.WindowEndDay
                && pace.PaceRatio >= BudgetPace.Tolerance;
            return new BudgetSummaryItemDto(
                b.Id,
                b.Category,
                _normalization.GetLabel(b.Category),
                limitUsd,
                spent,
                remaining,
                isOverBudget,
                "USD",
                pace.PaceRatio,
                pace.ProjectedMonthEndSpend,
                isOffPace);
        }).ToList();

        return new BudgetSummaryResponse(
            year,
            month,
            items,
            items.Sum(i => i.MonthlyLimit),
            items.Sum(i => i.Spent));
    }
}
