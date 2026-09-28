namespace FinanceSentry.Integration;

using FinanceSentry.Modules.Events.Domain.Ports;
using FinanceSentry.Modules.Research.Domain.Ports;

/// <summary>
/// Feature 049 - implements the Events module's <see cref="IUpcomingCorporateEventReader"/> over
/// Research's published <see cref="IEarningsCalendarReader"/> port (#673; live Yahoo read behind
/// its 6h cache). Lives in Integration so Modules.Events never references Modules.Research.
/// </summary>
public sealed class EventsCorporateCalendarAdapter(IEarningsCalendarReader earnings) : IUpcomingCorporateEventReader
{
    public async Task<IReadOnlyList<CorporateCalendarEntry>> GetForTickersAsync(
        IReadOnlyCollection<string> tickers, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (tickers.Count == 0)
        {
            return [];
        }

        // Feature 615 - a total fetch failure (every ticker errored) throws
        // EarningsCalendarProviderException, which GetUpcomingEventsQueryHandler's generic catch
        // turns into an "unavailable" source status. A partial failure still returns whichever
        // tickers succeeded.
        var events = await earnings.GetForTickersAsync([.. tickers], from, to, ct);
        return events
            .Select(e => new CorporateCalendarEntry(e.Ticker, e.EventType, e.EventDate, e.IsEstimate, e.Source))
            .ToList();
    }
}
