namespace FinanceSentry.Modules.Budgets.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Budgets.API.Responses;
using FinanceSentry.Modules.Budgets.Application.Services;
using FinanceSentry.Modules.Budgets.Domain.Exceptions;
using FinanceSentry.Modules.Budgets.Domain.Repositories;

public record GetBudgetSummaryQuery(Guid UserId, int? Year = null, int? Month = null)
    : IQuery<BudgetSummaryResponse>;

public class GetBudgetSummaryQueryHandler(
    IBudgetRepository budgets,
    IBudgetEstimateService estimates,
    TimeProvider? clock = null)
    : IQueryHandler<GetBudgetSummaryQuery, BudgetSummaryResponse>
{
    private readonly IBudgetRepository _budgets = budgets;
    private readonly IBudgetEstimateService _estimates = estimates;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<BudgetSummaryResponse> Handle(GetBudgetSummaryQuery request, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        var year = request.Year ?? now.Year;
        var month = request.Month ?? now.Month;

        if (month < 1 || month > 12 || year < 2020)
            throw new BudgetInvalidPeriodException();

        var overrides = await _budgets.GetByUserIdAsync(request.UserId, cancellationToken);
        return await _estimates.ComputeAsync(
            request.UserId, year, month, now.UtcDateTime, overrides, cancellationToken);
    }
}
