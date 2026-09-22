namespace FinanceSentry.Modules.Events.Domain.Ports;

/// <summary>Earnings and ex-dividend dates for a ticker set (adapter over Research's earnings calendar).</summary>
public interface IUpcomingCorporateEventReader
{
    Task<IReadOnlyList<CorporateCalendarEntry>> GetForTickersAsync(
        IReadOnlyCollection<string> tickers, DateOnly from, DateOnly to, CancellationToken ct = default);
}

/// <summary><paramref name="EventType"/> is the source vocabulary: <c>earnings</c>, <c>ex_dividend</c> or <c>dividend</c>.</summary>
public sealed record CorporateCalendarEntry(string Ticker, string EventType, DateOnly Date, bool IsEstimate, string Source);
