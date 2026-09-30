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
    [Description("Returns all active budgets, including spending and utilization for the requested period.")]
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
                b.Id.ToString(),
                b.CategoryLabel,
                period,
                b.MonthlyLimit,
                b.Spent,
                b.Currency,
                b.MonthlyLimit > 0m
                    ? Math.Round(b.Spent / b.MonthlyLimit * 100m, 2)
                    : 0m))
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
    decimal UtilizationPercent);
