namespace FinanceSentry.Integration;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Events.Domain.Ports;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;

/// <summary>
/// Feature 049 - implements the Events module's <see cref="IUpcomingCorporateEventReader"/> over
/// Research's <see cref="GetEarningsCalendarQuery"/> (live Yahoo read behind its 6h cache). Lives in
/// Integration so Modules.Events never references Modules.Research.
/// </summary>
public sealed class EventsCorporateCalendarAdapter(
    IQueryHandler<GetEarningsCalendarQuery, IReadOnlyList<EarningsEventDto>> earnings) : IUpcomingCorporateEventReader
{
    public async Task<IReadOnlyList<CorporateCalendarEntry>> GetForTickersAsync(
        IReadOnlyCollection<string> tickers, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (tickers.Count == 0)
        {
            return [];
        }

        var events = await earnings.Handle(new GetEarningsCalendarQuery([.. tickers], null, from, to, null), ct);
        return events
            .Select(e => new CorporateCalendarEntry(e.Ticker, e.EventType, e.EventDate, e.IsEstimate, e.Source))
            .ToList();
    }
}
