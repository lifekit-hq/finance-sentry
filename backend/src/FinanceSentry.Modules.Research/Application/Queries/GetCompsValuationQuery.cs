namespace FinanceSentry.Modules.Research.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Valuation;

/// <summary>
/// Trading-comps valuation for one equity ticker (#1269): where its multiples sit inside a peer set and
/// the per-share value the peer quartiles imply. The peer set is <c>get_valuation_snapshot</c>'s (Yahoo
/// similar symbols, at most six, or <see cref="Peers"/>). Computed on read from one Yahoo
/// <c>quoteSummary</c> call per ticker; no LLM, no stored row, no zero-filled gap.
/// </summary>
public record GetCompsValuationQuery(
    string Ticker,
    IReadOnlyList<string>? Peers) : IQuery<CompsValuationResult>;

public class GetCompsValuationQueryHandler(IValuationDataService valuation)
    : IQueryHandler<GetCompsValuationQuery, CompsValuationResult>
{
    private const decimal PercentScale = 100m;
    private const int MultipleDecimals = 2;
    private const int PriceDecimals = 2;
    private const int RankDecimals = 0;
    private const int PercentDecimals = 1;

    // One extracted figure: the value when usable, the typed reason when not. A P/E outlier carries both:
    // the number is real (shown for the target) but stays out of the peer statistics.
    private readonly record struct Figure(decimal? Value, string? Reason)
    {
        public bool Usable => Value is not null && Reason is null;
    }

    private sealed record Company(string Ticker, ValuationCurrentMetrics Metrics)
    {
        public ValuationCompsInputs? Inputs => Metrics.Comps;
    }

    // The EV -> equity bridge, resolved once for the target (null = a piece is missing; the flags say which).
    private sealed record Bridge(decimal? NetDebt, decimal? Shares, decimal? ToQuotedUnit);

    public async Task<CompsValuationResult> Handle(GetCompsValuationQuery query, CancellationToken ct)
    {
        var ticker = query.Ticker.Trim().ToUpperInvariant();

        var current = await valuation.GetCurrentMetricsAsync(ticker, ct);
        if (current is null)
        {
            return CompsValuationResult.Unavailable(ticker);
        }

        if (current.NotApplicable)
        {
            return CompsValuationResult.ForNonEquity(ticker);
        }

        var flags = new List<string>();
        var isCustom = query.Peers is { Count: > 0 };
        var symbols = ValuationPeerSymbols.Normalize(
            isCustom ? query.Peers! : await valuation.GetPeerSymbolsAsync(ticker, ct),
            ticker);

        var (peers, used, dropped) = await LoadPeersAsync(symbols, current.Industry, ct);
        if (dropped.Count > 0)
        {
            flags.Add(CompsFlags.PeersDropped);
        }

        if (used.Any(p => p.IndustryMatch == false))
        {
            flags.Add(CompsFlags.PeerIndustryMismatch);
        }

        var target = new Company(ticker, current);
        var isFinancial = IsFinancial(current.Industry);
        if (isFinancial)
        {
            flags.Add(CompsFlags.FinancialSector);
        }

        // Banks and insurers compare on P/E and P/B only, so they never need the EV -> equity bridge.
        var bridge = isFinancial ? new Bridge(null, null, null) : ResolveBridge(current, flags);

        var evToEbitda = isFinancial
            ? FinancialNotEvaluable()
            : Measure(target, peers, EvToEbitda, ImpliedFromEv(current, bridge, c => c.Inputs?.Ebitda), flags);
        var evToRevenue = isFinancial
            ? FinancialNotEvaluable()
            : Measure(target, peers, EvToRevenue, ImpliedFromEv(current, bridge, c => c.Inputs?.TotalRevenue), flags);
        var trailingPe = Measure(target, peers, c => PeFigure(c.Metrics.TrailingPe, c.Inputs?.TrailingEps), ImpliedFromRatio(current, c => c.Metrics.TrailingPe), flags);
        var forwardPe = Measure(target, peers, c => PeFigure(c.Metrics.ForwardPe, null), null, flags);
        var priceToBook = isFinancial
            ? Measure(target, peers, PriceToBook, ImpliedFromRatio(current, c => c.Inputs?.PriceToBook), flags)
            : null;
        var growth = Measure(target, peers, c => Scaled(c.Inputs?.RevenueGrowth), null, flags, PercentDecimals);
        var margin = Measure(target, peers, c => Scaled(c.Inputs?.EbitdaMargin), null, flags, PercentDecimals);

        if (evToEbitda.NotEvaluable && evToEbitda.NotEvaluableReason!.StartsWith(CompsReasons.TargetPrefix, StringComparison.Ordinal)
            && !evToRevenue.NotEvaluable)
        {
            flags.Add(CompsFlags.EvRevenueFallback);
        }

        if (current.IsStale)
        {
            flags.Add(CompsFlags.StalePrice);
        }

        var multiples = new CompsMultiples(evToEbitda, evToRevenue, trailingPe, forwardPe, priceToBook);
        var summary = BuildSummary(multiples, growth, margin, current.Price);

        var sources = new List<string> { "yahoo:quoteSummary" };
        if (!isCustom)
        {
            sources.Add("yahoo:recommendationsbysymbol");
        }

        return new CompsValuationResult(
            ticker,
            NotApplicable: false,
            current.Name,
            current.Sector,
            current.Industry,
            current.Currency,
            NotEvaluableReason: null,
            new CompsPeerSet(ValuationPeerSymbols.SetName(isCustom, current.Sector), used, dropped),
            summary,
            multiples,
            growth,
            margin,
            flags.Distinct().ToList(),
            sources,
            DateTimeOffset.UtcNow);
    }

    private async Task<(List<Company> Peers, List<CompsPeerUsed> Used, List<CompsPeerDropped> Dropped)> LoadPeersAsync(
        IReadOnlyList<string> symbols, string? targetIndustry, CancellationToken ct)
    {
        var peers = new List<Company>();
        var used = new List<CompsPeerUsed>();
        var dropped = new List<CompsPeerDropped>();
        foreach (var symbol in symbols)
        {
            var metrics = await valuation.GetCurrentMetricsAsync(symbol, ct);
            if (metrics is null)
            {
                dropped.Add(new CompsPeerDropped(symbol, CompsReasons.FetchFailed));
                continue;
            }

            if (metrics.NotApplicable)
            {
                dropped.Add(new CompsPeerDropped(symbol, CompsReasons.NotEquity));
                continue;
            }

            peers.Add(new Company(symbol, metrics));
            used.Add(new CompsPeerUsed(symbol, metrics.Name, metrics.Industry, IndustryMatch(targetIndustry, metrics.Industry)));
        }

        return (peers, used, dropped);
    }

    private static bool? IndustryMatch(string? target, string? peer)
        => string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(peer)
            ? null
            : string.Equals(target.Trim(), peer.Trim(), StringComparison.OrdinalIgnoreCase);

    // Banks and insurers: free cash flow and EV/EBITDA are not meaningful (the DCF refuses them for the same reason).
    private static bool IsFinancial(string? industry)
        => industry is not null
            && (industry.StartsWith("Banks", StringComparison.OrdinalIgnoreCase)
                || industry.StartsWith("Insurance", StringComparison.OrdinalIgnoreCase));

    private static CompsMeasure FinancialNotEvaluable() => new(
        Target: null, NotEvaluable: true, CompsReasons.FinancialSector, UsableCount: 0,
        null, null, null, null, null, null, null, []);

    // --- figure extraction (one rule per multiple; the same rule runs on the target and on every peer) ---

    private static Figure EvToEbitda(Company c)
    {
        if (c.Inputs?.EnterpriseValue is not { } ev || c.Inputs.Ebitda is not { } ebitda)
        {
            return new Figure(null, CompsReasons.MissingData);
        }

        return ebitda <= 0m
            ? new Figure(null, CompsReasons.NegativeEbitda)
            : ev <= 0m ? new Figure(null, CompsReasons.MissingData) : new Figure(Math.Round(ev / ebitda, MultipleDecimals), null);
    }

    private static Figure EvToRevenue(Company c)
        => c.Inputs?.EnterpriseValue is { } ev && c.Inputs.TotalRevenue is { } revenue && revenue > 0m && ev > 0m
            ? new Figure(Math.Round(ev / revenue, MultipleDecimals), null)
            : new Figure(null, CompsReasons.MissingData);

    private static Figure PriceToBook(Company c)
        => c.Inputs?.PriceToBook is not { } pb
            ? new Figure(null, CompsReasons.MissingData)
            : pb <= 0m ? new Figure(null, CompsReasons.NegativeBookValue) : new Figure(Math.Round(pb, MultipleDecimals), null);

    private static Figure PeFigure(decimal? pe, decimal? eps)
    {
        if (pe is null)
        {
            return eps is <= 0m
                ? new Figure(null, CompsReasons.NegativeEarnings)
                : new Figure(null, CompsReasons.MissingData);
        }

        if (pe <= 0m)
        {
            return new Figure(null, CompsReasons.NegativeEarnings);
        }

        var value = Math.Round(pe.Value, MultipleDecimals);
        return value > CompsMath.PeOutlierThreshold ? new Figure(value, CompsReasons.PeOutlier) : new Figure(value, null);
    }

    private static Figure Scaled(decimal? fraction)
        => fraction is { } f ? new Figure(f * PercentScale, null) : new Figure(null, CompsReasons.MissingData);

    // --- one compared figure: peer distribution, target rank, implied value ---

    private static CompsMeasure Measure(
        Company target,
        IReadOnlyList<Company> peers,
        Func<Company, Figure> extract,
        Func<decimal, decimal?>? implied,
        List<string> flags,
        int decimals = MultipleDecimals)
    {
        var peerFigures = peers.Select(p => (p.Ticker, Figure: extract(p))).ToList();
        var usable = peerFigures.Where(p => p.Figure.Usable).Select(p => Math.Round(p.Figure.Value!.Value, decimals)).ToList();
        var excluded = peerFigures
            .Where(p => !p.Figure.Usable)
            .Select(p => new CompsExclusion(p.Ticker, p.Figure.Reason!))
            .ToList();
        if (excluded.Any(e => e.Reason == CompsReasons.PeOutlier))
        {
            flags.Add(CompsFlags.PeOutlierExcluded);
        }

        var targetFigure = extract(target);
        if (targetFigure.Reason == CompsReasons.PeOutlier)
        {
            flags.Add(CompsFlags.TargetPeOutlier);
        }

        var distribution = CompsMath.Describe(usable);
        string? reason = null;
        if (distribution is null)
        {
            reason = CompsReasons.InsufficientPeers;
        }
        else if (targetFigure.Value is null)
        {
            reason = CompsReasons.TargetPrefix + targetFigure.Reason;
        }

        if (reason is not null)
        {
            flags.Add(CompsFlags.MultipleNotEvaluable);
        }

        if (distribution is null)
        {
            return new CompsMeasure(
                targetFigure.Value, NotEvaluable: true, reason, usable.Count,
                null, null, null, null, null, null, null, excluded);
        }

        var rank = targetFigure.Value is { } t
            ? CompsMath.PercentileRank(t, usable) is { } r ? Math.Round(r, RankDecimals, MidpointRounding.AwayFromZero) : (decimal?)null
            : null;

        CompsImpliedValue? impliedPerShare = null;
        if (implied is not null && targetFigure.Value is not null)
        {
            impliedPerShare = new CompsImpliedValue(
                Price(implied(distribution.P25)),
                Price(implied(distribution.Median)),
                Price(implied(distribution.P75)));
        }

        return new CompsMeasure(
            targetFigure.Value,
            NotEvaluable: reason is not null,
            reason,
            distribution.Count,
            Math.Round(distribution.Min, decimals),
            Math.Round(distribution.P25, decimals),
            Math.Round(distribution.Median, decimals),
            Math.Round(distribution.P75, decimals),
            Math.Round(distribution.Max, decimals),
            rank,
            impliedPerShare,
            excluded);
    }

    private static decimal? Price(decimal? value) => value is { } v ? Math.Round(v, PriceDecimals) : null;

    // --- implied per-share value ---

    // A ratio multiple (P/E, P/B) scales the price directly, so no currency or share count is involved.
    private static Func<decimal, decimal?>? ImpliedFromRatio(ValuationCurrentMetrics target, Func<Company, decimal?> targetRatio)
    {
        var ratio = targetRatio(new Company(target.Ticker, target));
        return target.Price is { } price && ratio is > 0m
            ? multiple => price * multiple / ratio.Value
            : null;
    }

    // An EV multiple implies an enterprise value on the target's own EBITDA / revenue, then the same net-debt
    // bridge Yahoo's EV is built from: (EV - (debt - cash)) / shares, in the reporting currency, converted once
    // to the quote currency (and its minor unit, so it sits beside the quoted price).
    private static Func<decimal, decimal?>? ImpliedFromEv(
        ValuationCurrentMetrics target, Bridge bridge, Func<Company, decimal?> metric)
    {
        var amount = metric(new Company(target.Ticker, target));
        if (amount is not > 0m || bridge.NetDebt is not { } netDebt || bridge.Shares is not { } shares || bridge.ToQuotedUnit is not { } toQuoted)
        {
            return null;
        }

        return multiple => CompsMath.PerShareFromEnterpriseValue(multiple * amount.Value, netDebt, shares) * toQuoted;
    }

    private static Bridge ResolveBridge(ValuationCurrentMetrics target, List<string> flags)
    {
        var inputs = target.Comps;
        decimal? netDebt = inputs?.TotalDebt is { } debt && inputs.TotalCash is { } cash ? debt - cash : null;
        if (netDebt is null)
        {
            flags.Add(CompsFlags.NetDebtUnavailable);
        }

        var (quoteCurrency, divisor) = MinorUnitCurrency.Normalize(target.Currency);

        decimal? shares = inputs?.SharesOutstanding is > 0m ? inputs.SharesOutstanding : null;
        if (shares is null && target.MarketCap is > 0m && target.Price is > 0m)
        {
            shares = target.MarketCap / (target.Price / divisor);
            flags.Add(CompsFlags.SharesFromMarketCap);
        }

        decimal? toQuoted = divisor;
        var reporting = inputs?.FinancialCurrency?.Trim();
        if (!string.IsNullOrEmpty(reporting) && !string.IsNullOrEmpty(quoteCurrency)
            && !string.Equals(reporting, quoteCurrency, StringComparison.OrdinalIgnoreCase))
        {
            if (CurrencyConverter.IsKnown(reporting) && CurrencyConverter.IsKnown(quoteCurrency))
            {
                toQuoted = divisor * CurrencyConverter.ToUsd(1m, reporting) / CurrencyConverter.ToUsd(1m, quoteCurrency);
                flags.Add(CompsFlags.FxConverted);
            }
            else
            {
                toQuoted = null;
                flags.Add(CompsFlags.FxUnknown);
            }
        }

        return new Bridge(netDebt, shares, toQuoted);
    }

    // --- summary ---

    private static CompsSummary BuildSummary(CompsMultiples m, CompsMeasure growth, CompsMeasure margin, decimal? price)
    {
        var candidates = new (string Basis, CompsMeasure? Measure)[]
        {
            ("evToEbitda", m.EvToEbitda),
            ("trailingPe", m.TrailingPe),
            ("evToRevenue", m.EvToRevenue),
            ("priceToBook", m.PriceToBook),
        };

        string? basis = null;
        decimal? median = null;
        foreach (var (name, measure) in candidates)
        {
            if (measure?.ImpliedPerShare?.Median is { } value)
            {
                basis = name;
                median = value;
                break;
            }
        }

        var upside = median is { } implied && price is > 0m
            ? Math.Round((implied / price.Value - 1m) * PercentScale, PercentDecimals)
            : (decimal?)null;

        return new CompsSummary(
            m.EvToEbitda.PercentileRank,
            m.EvToRevenue.PercentileRank,
            m.TrailingPe.PercentileRank,
            m.PriceToBook?.PercentileRank,
            growth.PercentileRank,
            margin.PercentileRank,
            basis,
            median,
            upside,
            price);
    }
}
