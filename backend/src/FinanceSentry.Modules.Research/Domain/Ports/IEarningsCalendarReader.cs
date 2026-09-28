namespace FinanceSentry.Modules.Research.Domain.Ports;

/// <summary>
/// Published read port (#673): the corporate earnings calendar for an explicit ticker list, for
/// cross-module readers such as the Events module. Implemented inside Research; the
/// FinanceSentry.Integration adapter reaches Research only through this interface.
/// </summary>
public interface IEarningsCalendarReader
{
    /// <summary>
    /// Earnings events for <paramref name="tickers"/> dated within [<paramref name="from"/>,
    /// <paramref name="to"/>]. A partial provider failure returns whichever tickers succeeded; a total
    /// failure (every ticker errored) throws <see cref="EarningsCalendarProviderException"/> rather
    /// than returning an empty list that would read as "no events".
    /// </summary>
    Task<IReadOnlyList<EarningsCalendarItem>> GetForTickersAsync(
        IReadOnlyList<string> tickers, DateOnly from, DateOnly to, CancellationToken ct = default);
}

/// <summary>One earnings calendar event as a cross-module reader sees it.</summary>
public sealed record EarningsCalendarItem(
    string Ticker,
    string EventType,
    DateOnly EventDate,
    bool IsEstimate,
    string Source);
