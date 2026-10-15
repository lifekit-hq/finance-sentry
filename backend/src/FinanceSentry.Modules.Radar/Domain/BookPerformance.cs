namespace FinanceSentry.Modules.Radar.Domain;

using FinanceSentry.Core.Headlines;

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
/// <para>
/// <c>HoldingsBasedReturn</c> is the flow-neutral companion: the book's price return from revaluing each
/// capture's quantities at the next capture's prices and chaining, so deposits and purchases from idle cash
/// read as 0%. It excludes dividends, so it is compared with <c>SpyPriceReturn</c>, SPY's change in close
/// (not adjusted close), measured between the first and last capture the index covers; <c>HoldingsBasedDelta</c> is the difference. When there is
/// no number, <c>HoldingsBasedUnavailableReason</c> says why (never a 0) and, for a missing price,
/// <c>HoldingsBasedMissingPriceSymbols</c> names the positions.
/// </para>
/// <para>
/// <c>Book</c> names the book side as a typed <see cref="Headline"/> (VALUE_CHANGE over START_VALUE on
/// <c>invested_brokerage</c>, USD): its amount is the dollar change, its percent the same change in percentage
/// points, and its quality and reasons carry the end rows' stale, reconstructed and FX flags, or say why there is
/// no figure (SHORT_HISTORY, ZERO_BASE).
/// </para>
/// </summary>
public sealed record PeriodTwr(
    BookPerformancePeriod Period,
    DateOnly Since,
    decimal? BookTwr,
    decimal? SpyTwr,
    decimal? Delta,
    string? Verdict,
    decimal? HoldingsBasedReturn = null,
    decimal? SpyPriceReturn = null,
    decimal? HoldingsBasedDelta = null,
    HoldingsReturnUnavailableReason? HoldingsBasedUnavailableReason = null,
    IReadOnlyList<string>? HoldingsBasedMissingPriceSymbols = null,
    Headline? Book = null);

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
