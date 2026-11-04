using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Budgets.Application.Queries;
using FinanceSentry.Modules.Budgets.API.Responses;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class GetBudgetStatusTool(
    IQueryHandler<GetBudgetSummaryQuery, BudgetSummaryResponse> budgetSummaryHandler,
    IIdentityResolver identity,
    ILogger<GetBudgetStatusTool> logger)
{
    private readonly IQueryHandler<GetBudgetSummaryQuery, BudgetSummaryResponse> _budgetSummaryHandler = budgetSummaryHandler;
    private readonly IIdentityResolver _identity = identity;
    private readonly ILogger<GetBudgetStatusTool> _logger = logger;

    [McpServerTool(Name = "get_budget_status")]
    [Description("Returns the monthly estimate of every spending line (a median of the person's own usable past months, a detected commitment where there is one, or the amount they set by hand), including spending, utilization and month-end pace (pace ratio, projected month-end spend, off-pace flag) for the requested period. A line with no estimate yet reports a limit of 0.")]
    public async Task<IReadOnlyList<BudgetStatusEntry>> ExecuteAsync(
        [Description("Year of the budget period (e.g. 2024). Defaults to the current year.")] int? year = null,
        [Description("Month of the budget period (1–12). Defaults to the current month.")] int? month = null,
        CancellationToken cancellationToken = default)
    {
        var effective = _identity.GetUserId();
        if (effective is null) return [];
        var userIdVal = effective.Value;

        BudgetSummaryResponse summary;
        try
        {
            summary = await _budgetSummaryHandler.Handle(
                new GetBudgetSummaryQuery(userIdVal, year, month),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Budget summary query unavailable for user {UserId}; returning empty list.", userIdVal);
            return [];
        }

        var period = $"{summary.Year:D4}-{summary.Month:D2}";

        return summary.Items
            .Select(b => new BudgetStatusEntry(
                b.OverrideId?.ToString() ?? b.Category,
                b.CategoryLabel,
                period,
                b.Estimate ?? 0m,
                b.Spent,
                b.Currency,
                b.Estimate is > 0m
                    ? Math.Round(b.Spent / b.Estimate.Value * 100m, 2)
                    : 0m,
                b.PaceRatio,
                b.ProjectedMonthEndSpend,
                b.IsOffPace))
            .ToList();
    }
}

public sealed record BudgetStatusEntry(
    string BudgetId,
    string Name,
    string Period,
    decimal LimitAmount,
    decimal SpentAmount,
    string Currency,
    decimal UtilizationPercent,
    decimal PaceRatio,
    decimal ProjectedMonthEndSpend,
    bool IsOffPace);
