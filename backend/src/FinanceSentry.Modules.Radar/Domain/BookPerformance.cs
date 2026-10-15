namespace FinanceSentry.Modules.Radar.Domain;

/// <summary>Lookback window for the book-vs-SPY value-change computation.</summary>
public enum BookPerformancePeriod
{
    OneWeek,
    OneMonth,
    ThreeMonths,
    OneYear,
}

/// <summary>
/// Single-period change in invested value (includes deposits) for the book (IBKR brokerage portfolio,
/// idle cash excluded) next to SPY's adjusted-close change. Despite the <c>Twr</c> names, none of these
/// fields is a time-weighted return: <c>BookTwr</c> is last / first - 1 over invested value with no
/// contribution adjustment. The names are kept for contract stability (MCP tool output).
/// Null fields indicate insufficient price or invested-value history for the period.
/// </summary>
public sealed record PeriodTwr(
    BookPerformancePeriod Period,
    DateOnly Since,
    decimal? BookTwr,
    decimal? SpyTwr,
    decimal? Delta,
    string? Verdict);

/// <summary>
/// Book-vs-SPY change-in-invested-value scoreboard across the four standard lookback windows.
/// </summary>
public sealed record BookPerformanceResult(
    IReadOnlyList<PeriodTwr> Periods,
    DateOnly AsOf)
{
    public static BookPerformanceResult Empty(DateOnly asOf) =>
        new([], asOf);
}
