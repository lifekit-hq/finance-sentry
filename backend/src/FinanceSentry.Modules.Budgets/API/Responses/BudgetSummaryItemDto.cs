namespace FinanceSentry.Modules.Budgets.API.Responses;

public record BudgetSummaryItemDto(
    Guid Id,
    string Category,
    string CategoryLabel,
    decimal MonthlyLimit,
    decimal Spent,
    decimal Remaining,
    bool IsOverBudget,
    string Currency,
    decimal PaceRatio,
    decimal ProjectedMonthEndSpend,
    bool IsOffPace);
