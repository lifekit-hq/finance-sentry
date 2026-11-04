namespace FinanceSentry.Modules.Budgets.API.Responses;

/// <summary>A month dropped from the history the estimates learn from, and why (<c>excluded</c> or <c>coverage-gap</c>).</summary>
/// <param name="Month">"yyyy-MM".</param>
public record SkippedEstimateMonthDto(string Month, string Reason);

/// <summary>A month's estimates, USD.</summary>
/// <param name="TotalEstimate">Sum of the lines' effective amounts.</param>
/// <param name="ExpectedMonthEnd">Where the month is expected to finish: spent so far plus what is still due.</param>
/// <param name="HasEnoughHistory">Whether the history window holds <paramref name="MinHistoryMonths"/> usable months; below that no line is suggested from history.</param>
/// <param name="HistoryMonthsUsed">The usable months the suggestions were learned from.</param>
/// <param name="SkippedMonths">The months in the lookback that were left out, so the screen can say so.</param>
public record BudgetSummaryResponse(
    int Year,
    int Month,
    IReadOnlyList<BudgetSummaryItemDto> Items,
    decimal TotalEstimate,
    decimal TotalSpent,
    decimal ExpectedMonthEnd,
    bool HasEnoughHistory,
    int HistoryMonthsUsed,
    int MinHistoryMonths,
    IReadOnlyList<SkippedEstimateMonthDto> SkippedMonths);
