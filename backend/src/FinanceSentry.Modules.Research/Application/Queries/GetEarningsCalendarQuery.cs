namespace FinanceSentry.Modules.Research.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Domain;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain.Repositories;

// Tickers explicit? Use them. Otherwise, when a UserId is supplied, resolve the user's own
// universe: equity brokerage holdings + watchlist. Crypto has no earnings, so it is excluded.
// SurfaceProviderFailure defaults false so every caller but the Events module (feature 615) keeps
// today's behaviour: a provider fetch failure just yields no events, never an exception.
public record GetEarningsCalendarQuery(
    IReadOnlyList<string>? Tickers,
    Guid? UserId,
    DateOnly From,
    DateOnly To,
    string? EventType,
    bool SurfaceProviderFailure = false) : IQuery<IReadOnlyList<EarningsEventDto>>;

public class GetEarningsCalendarQueryHandler(
    IEarningsCalendarService svc,
    IBrokerageHoldingsReader brokerageReader,
    IWatchlistRepository watchlistRepo)
    : IQueryHandler<GetEarningsCalendarQuery, IReadOnlyList<EarningsEventDto>>
{
    public async Task<IReadOnlyList<EarningsEventDto>> Handle(GetEarningsCalendarQuery query, CancellationToken ct)
    {
        var tickers = await ResolveTickersAsync(query, ct);
        if (tickers.Count == 0)
        {
            return [];
        }

        var events = await svc.GetForTickersAsync(
            tickers, query.From, query.To, query.EventType, ct, query.SurfaceProviderFailure);
        return events
            .Select(e => new EarningsEventDto(e.Ticker, e.EventType, e.EventDate, e.IsEstimate, e.Source))
            .ToList();
    }

    private async Task<IReadOnlyCollection<string>> ResolveTickersAsync(GetEarningsCalendarQuery query, CancellationToken ct)
    {
        if (query.Tickers is { Count: > 0 })
        {
            return query.Tickers;
        }

        if (query.UserId is not Guid userId)
        {
            return [];
        }

        var tickers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var h in await brokerageReader.GetHoldingsAsync(userId, ct))
        {
            if (AssetClassNormalizer.Normalize(h.InstrumentType) == AssetClassNormalizer.Equities)
            {
                tickers.Add(h.Symbol);
            }
        }

        foreach (var w in await watchlistRepo.ListAsync(userId, ct))
        {
            tickers.Add(w.Ticker);
        }

        return tickers;
    }
}
