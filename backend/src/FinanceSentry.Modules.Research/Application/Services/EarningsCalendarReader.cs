namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Domain.Ports;

/// <summary><see cref="IEarningsCalendarReader"/> impl over Research's own <see cref="GetEarningsCalendarQuery"/>.</summary>
public sealed class EarningsCalendarReader(
    IQueryHandler<GetEarningsCalendarQuery, IReadOnlyList<EarningsEventDto>> earnings) : IEarningsCalendarReader
{
    public async Task<IReadOnlyList<EarningsCalendarItem>> GetForTickersAsync(
        IReadOnlyList<string> tickers, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        // Feature 615 - opt into the batch failure signal: a total fetch failure throws
        // EarningsCalendarProviderException instead of reading as "no events".
        var events = await earnings.Handle(
            new GetEarningsCalendarQuery(tickers, null, from, to, null, SurfaceProviderFailure: true), ct);
        return events
            .Select(e => new EarningsCalendarItem(e.Ticker, e.EventType, e.EventDate, e.IsEstimate, e.Source))
            .ToList();
    }
}
