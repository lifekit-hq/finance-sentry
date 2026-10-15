namespace FinanceSentry.Modules.Radar.Domain;

using FinanceSentry.Core.Headlines;

/// <summary>Lookback window for the book-vs-SPY computation.</summary>
public enum BookPerformancePeriod
{
    OneWeek,
    OneMonth,
    ThreeMonths,
    OneYear,
}

/// <summary>
/// Single-period book-vs-SPY scoreboard row. <c>BookTwr</c> is M3, the time-weighted return of the brokerage
/// sleeve (IBKR + Inzhur, idle broker cash included, so a purchase from cash is not a gain), chained daily over
/// the snapshot closes with each deposit and withdrawal the brokers report taken out (finance-framework §3.4), and
/// with the day a provider was linked bridged as capital, not return. <c>SpyTwr</c> is SPY's adjusted-close return
/// over the identical window (<c>BookTwrFrom</c> to <c>BookTwrTo</c>, M9), so <c>Delta</c> is a like-for-like
/// excess return. With no measurable book window, <c>SpyTwr</c> runs from the first bar on or after
/// <c>Since</c> and there is no <c>Delta</c>. <c>BookTwrReasons</c> qualify the TWR: <c>UNKNOWN_FLOW</c> names a
/// provider that has never reported a deposit or withdrawal (its deposits would read as return, so the figure is
/// an estimate), <c>SCOPE_CHANGED</c> says a link was bridged, and the end rows' stale, reconstructed and FX flags
/// carry over. Null fields indicate insufficient price or value history for the period.
/// <para>
/// <c>HoldingsBasedReturn</c> is the flow-neutral companion: the book's price return from revaluing each
/// capture's quantities at the next capture's prices and chaining, so deposits and purchases from idle cash
/// read as 0%. It excludes dividends, so it is compared with <c>SpyPriceReturn</c>, SPY's change in close
/// (not adjusted close), measured between the first and last capture the index covers; <c>HoldingsBasedDelta</c> is the difference. When there is
/// no number, <c>HoldingsBasedUnavailableReason</c> says why (never a 0) and, for a missing price,
/// <c>HoldingsBasedMissingPriceSymbols</c> names the positions.
/// </para>
/// <para>
/// <c>Book</c> names the invested value's change as a typed <see cref="Headline"/> (VALUE_CHANGE over START_VALUE
/// on <c>invested_brokerage</c>, USD; deposits included): its amount is the dollar change, its percent the same
/// change in percentage points, and its quality and reasons carry the end rows' stale, reconstructed and FX flags,
/// or say why there is no figure (SHORT_HISTORY, ZERO_BASE, or SCOPE_CHANGED when a provider was linked inside it).
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
    Headline? Book = null,
    DateOnly? BookTwrFrom = null,
    DateOnly? BookTwrTo = null,
    IReadOnlyList<HeadlineReason>? BookTwrReasons = null);

/// <summary>
/// Book-vs-SPY time-weighted scoreboard across the four standard lookback windows.
/// </summary>
public sealed record BookPerformanceResult(
    IReadOnlyList<PeriodTwr> Periods,
    DateOnly AsOf)
{
    public static BookPerformanceResult Empty(DateOnly asOf) =>
        new([], asOf);
}
