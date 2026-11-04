namespace FinanceSentry.Modules.Budgets.API.Responses;

/// <summary>One line of a month's estimate, USD.</summary>
/// <param name="OverrideId">The hand-edited amount sitting over the suggestion (an override row), or null when the line runs on the suggestion.</param>
/// <param name="Estimate">The effective amount: the override when there is one, otherwise the suggestion; null when neither exists.</param>
/// <param name="SuggestedAmount">What the line is estimated at without a hand edit, and what "reset" returns to; null when the history cannot suggest one.</param>
/// <param name="Source"><c>override</c>, <c>history</c>, <c>commitment</c> or <c>none</c> (spend with no estimate yet).</param>
/// <param name="ExpectedMonthEnd">Where the line is expected to finish the month; what was spent for a past month.</param>
public record BudgetSummaryItemDto(
    Guid? OverrideId,
    string Category,
    string CategoryLabel,
    decimal? Estimate,
    decimal? SuggestedAmount,
    string Source,
    decimal Spent,
    decimal Remaining,
    bool IsOverBudget,
    string Currency,
    decimal PaceRatio,
    decimal ProjectedMonthEndSpend,
    bool IsOffPace,
    decimal ExpectedMonthEnd);
