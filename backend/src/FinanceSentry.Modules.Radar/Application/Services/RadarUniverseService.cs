namespace FinanceSentry.Modules.Radar.Application.Services;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Radar.Domain;
using FinanceSentry.Modules.Radar.Domain.Repositories;
using Microsoft.Extensions.Options;

/// <summary>
/// Composes the Radar universe: seed members (benchmark + sectors + industry) ∪ equity holdings
/// (across all active users, filtered to <c>STK</c>) ∪ watchlist tickers ∪ thesis tickers ∪ coverage
/// universe tickers ∪ — when
/// <see cref="RadarOptions.BroadUniverseEnabled"/> is set — the stage-1 scan shortlist (#558).
/// Membership is persisted and auto-synced; departed tickers are de-activated (history retained).
///
/// The shortlist is what bounds the cost of everything downstream: bars are ingested and market
/// structure computed for exactly these members, so widening the scan past the book costs tens of
/// tickers a run, not a whole index.
/// </summary>
public sealed class RadarUniverseService(
    IRadarUniverseRepository universe,
    IBrokerageHoldingsReader brokerageReader,
    IWatchlistReader watchlistReader,
    IThesisTickerReader thesisReader,
    IBankingTotalsReader bankingTotals,
    IOptions<RadarOptions> options,
    IScanShortlistSource? shortlistSource = null,
    ICoverageUniverseReader? coverageReader = null) : IRadarUniverseService
{
    private const string EquityInstrumentType = "STK";

    private readonly RadarOptions _options = options.Value;

    public async Task<IReadOnlyList<RadarUniverseMember>> GetActiveAsync(CancellationToken ct = default)
        => await universe.ListActiveAsync(ct);

    public async Task<IReadOnlyList<RadarUniverseMember>> SyncAsync(CancellationToken ct = default)
    {
        var resolved = new Dictionary<string, RadarUniverseMember>(StringComparer.OrdinalIgnoreCase);

        AddSeed(resolved, _options.BenchmarkTickers, UniverseKind.Benchmark);
        AddSeed(resolved, _options.SectorTickers, UniverseKind.Sector);
        AddSeed(resolved, _options.IndustryTickers, UniverseKind.Industry);

        var userIds = await bankingTotals.GetActiveUserIdsAsync(ct);
        foreach (var userId in userIds)
        {
            var holdings = await brokerageReader.GetHoldingsAsync(userId, ct);
            foreach (var holding in holdings.Where(h =>
                         string.Equals(h.InstrumentType, EquityInstrumentType, StringComparison.OrdinalIgnoreCase)))
            {
                Add(resolved, holding.Symbol, UniverseKind.Holding, UniverseSource.Auto);
            }

            var watchlist = await watchlistReader.ListTickersAsync(userId, ct);
            foreach (var ticker in watchlist)
            {
                Add(resolved, ticker, UniverseKind.Watchlist, UniverseSource.Auto);
            }

            // After holdings and watchlist so an owned or watched name keeps its ownership kind.
            foreach (var ticker in await thesisReader.ListTickersAsync(userId, ct))
            {
                Add(resolved, ticker, UniverseKind.Thesis, UniverseSource.Auto);
            }

            // Coverage members are graded from these bars (the Ledger scoreboard), so a name added by the
            // pipeline that is neither held, watched nor under a thesis still has to be ingested.
            if (coverageReader is not null)
            {
                foreach (var ticker in await coverageReader.ListTickersAsync(userId, ct))
                {
                    Add(resolved, ticker, UniverseKind.Coverage, UniverseSource.Auto);
                }
            }
        }

        // Added last so an owned or watched name keeps its ownership kind (first write wins).
        if (_options.BroadUniverseEnabled && shortlistSource is not null)
        {
            foreach (var ticker in await shortlistSource.GetShortlistAsync(ct))
            {
                Add(resolved, ticker, UniverseKind.IndexConstituent, UniverseSource.Auto);
            }
        }

        await universe.UpsertMembersAsync(resolved.Values.ToList(), ct);

        // De-activate any previously-active auto member no longer resolved this run.
        var all = await universe.ListAllAsync(ct);
        var departed = all
            .Where(m => m.Active && m.Source == UniverseSource.Auto && !resolved.ContainsKey(m.Ticker))
            .Select(m => m.Ticker)
            .ToList();
        if (departed.Count > 0)
        {
            await universe.DeactivateAsync(departed, ct);
        }

        return await universe.ListActiveAsync(ct);
    }

    private static void AddSeed(
        Dictionary<string, RadarUniverseMember> resolved, IEnumerable<string> tickers, UniverseKind kind)
    {
        foreach (var ticker in tickers)
        {
            Add(resolved, ticker, kind, UniverseSource.Seed);
        }
    }

    private static void Add(
        Dictionary<string, RadarUniverseMember> resolved, string ticker, UniverseKind kind, UniverseSource source)
    {
        var normalized = ticker.Trim().ToUpperInvariant();
        if (normalized.Length == 0 || resolved.ContainsKey(normalized))
        {
            return;
        }

        resolved[normalized] = new RadarUniverseMember
        {
            Ticker = normalized,
            Kind = kind,
            Source = source,
            Active = true,
        };
    }
}
