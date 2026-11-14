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
    ICryptoHoldingsReader crypto,
    ICoverageRepository coverage)
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

    /// <summary>
    /// The coverage universe ("our own index", discovery funnel stage 1): the tickers the pipeline keeps in view. The
    /// detectors read these names for events (filings, earnings, news) but raise no alert of their own for a name that
    /// is not otherwise in their universe; a hit moves the member's <c>LastEventAt</c> and is reviewed by the coverage
    /// rules. A coverage ticker naming a crypto asset the user holds is left out for the reason
    /// <see cref="EarningsTickersAsync"/> gives. Held names stay ahead of the rest in
    /// <see cref="CoverageTickersByPriorityAsync"/>.
    /// </summary>
    public async Task<IReadOnlySet<string>> CoverageTickersAsync(Guid userId, CancellationToken ct = default)
        => (await CoverageTickersByPriorityAsync(userId, ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The coverage tickers with the held ones first, then the rest in ticker order: the order a per-run budget fills in
    /// so the book is never starved by the wider list.
    /// </summary>
    public async Task<IReadOnlyList<string>> CoverageTickersByPriorityAsync(Guid userId, CancellationToken ct = default)
    {
        var members = await coverage.ListMembersUnscopedAsync(userId, ct);
        if (members.Count == 0)
        {
            return [];
        }

        var cryptoAssets = (await crypto.GetHoldingsAsync(userId, ct))
            .Select(h => h.Asset)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var held = await EquityHoldingsAsync(userId, ct);

        return
        [
            .. members
                .Select(m => m.Ticker.Trim().ToUpperInvariant())
                .Where(t => t.Length > 0 && !cryptoAssets.Contains(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => held.Contains(t) ? 0 : 1)
                .ThenBy(t => t, StringComparer.Ordinal),
        ];
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
