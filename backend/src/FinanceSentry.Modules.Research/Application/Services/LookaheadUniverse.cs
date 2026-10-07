namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Domain.Repositories;

/// <summary>
/// The ticker sets the look-ahead detectors watch (#698), in one place so the detectors and the
/// silence monitor that checks them always agree on what "non-empty inputs" means.
/// </summary>
public sealed class LookaheadUniverse(
    IBrokerageHoldingsReader brokerage,
    IWatchlistReader watchlist,
    IThesisRepository theses,
    ICryptoHoldingsReader crypto)
{
    private const string EquityInstrumentType = "STK";

    /// <summary>
    /// Equity holdings, watchlist and thesis tickers. A thesis ticker naming a crypto asset the user
    /// holds (SOL, ETH, XRP) is left out: crypto has no earnings, and the bare symbol resolves to an
    /// unrelated listed equity or fund on the calendar feed, so keeping it would only mis-alert.
    /// </summary>
    public async Task<IReadOnlySet<string>> EarningsTickersAsync(Guid userId, CancellationToken ct = default)
    {
        var tickers = await EquityHoldingsAsync(userId, ct);

        foreach (var ticker in await watchlist.ListTickersAsync(userId, ct))
        {
            tickers.Add(ticker);
        }

        var cryptoAssets = (await crypto.GetHoldingsAsync(userId, ct))
            .Select(h => h.Asset)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var thesis in await theses.ListUnscopedAsync(userId, ct))
        {
            if (!string.IsNullOrWhiteSpace(thesis.Ticker) && !cryptoAssets.Contains(thesis.Ticker))
            {
                tickers.Add(thesis.Ticker);
            }
        }

        return tickers;
    }

    /// <summary>Equity holdings alone: the names the analyst rating-change alert covers by default.</summary>
    public async Task<IReadOnlySet<string>> HeldEquityTickersAsync(Guid userId, CancellationToken ct = default)
        => await EquityHoldingsAsync(userId, ct);

    /// <summary>The watchlist alone: covered by the analyst rating-change alert only when the user opts in.</summary>
    public async Task<IReadOnlySet<string>> WatchlistTickersAsync(Guid userId, CancellationToken ct = default)
        => (await watchlist.ListTickersAsync(userId, ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Equity holdings, thesis tickers and their invalidation-trigger proxy tickers.</summary>
    public async Task<IReadOnlySet<string>> FilingTickersAsync(Guid userId, CancellationToken ct = default)
    {
        var tickers = await EquityHoldingsAsync(userId, ct);

        foreach (var thesis in await theses.ListUnscopedAsync(userId, ct))
        {
            if (!string.IsNullOrWhiteSpace(thesis.Ticker))
            {
                tickers.Add(thesis.Ticker);
            }

            foreach (var trigger in thesis.InvalidationTriggers)
            {
                if (!string.IsNullOrWhiteSpace(trigger.ProxyTicker))
                {
                    tickers.Add(trigger.ProxyTicker);
                }
            }
        }

        return tickers;
    }

    private async Task<HashSet<string>> EquityHoldingsAsync(Guid userId, CancellationToken ct)
    {
        var tickers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var holding in await brokerage.GetHoldingsAsync(userId, ct))
        {
            if (string.Equals(holding.InstrumentType, EquityInstrumentType, StringComparison.OrdinalIgnoreCase))
            {
                tickers.Add(holding.Symbol);
            }
        }

        return tickers;
    }
}
