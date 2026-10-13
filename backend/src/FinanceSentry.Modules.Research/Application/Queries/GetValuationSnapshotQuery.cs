namespace FinanceSentry.Modules.Research.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;

/// <summary>
/// Valuation snapshot for one equity ticker: current metrics vs the ticker's own trailing-P/E history
/// and a named peer set, plus consensus target and implied upside (feature 030, FR-005). <see
/// cref="Peers"/> overrides the default sector/industry peer set. Non-equity tickers return an explicit
/// not-applicable result. Every call persists a <c>valuation_snapshots</c> row (FR-006).
/// </summary>
public record GetValuationSnapshotQuery(
    string Ticker,
    IReadOnlyList<string>? Peers) : IQuery<ValuationSnapshotResult>;

public class GetValuationSnapshotQueryHandler(
    IValuationDataService valuation,
    IValuationHistoryService history,
    IValuationSnapshotRepository snapshots)
    : IQueryHandler<GetValuationSnapshotQuery, ValuationSnapshotResult>
{
    private const int MaxPeers = 6;
    private const int PercentScale = 100;

    public async Task<ValuationSnapshotResult> Handle(GetValuationSnapshotQuery query, CancellationToken ct)
    {
        var ticker = query.Ticker.Trim().ToUpperInvariant();

        var current = await valuation.GetCurrentMetricsAsync(ticker, ct);
        if (current is null)
        {
            return ValuationSnapshotResult.ForNonEquity(ticker) with { NotApplicable = false };
        }

        if (current.NotApplicable)
        {
            return ValuationSnapshotResult.ForNonEquity(ticker);
        }

        var trailingHistory = await history.GetTrailingPeHistoryAsync(ticker, ct);

        var metrics = new ValuationMetricsDto(
            new MetricValue(current.TrailingPe, trailingHistory.FiveYearAvg, trailingHistory.WindowYears),
            new MetricValue(current.ForwardPe, HistoryUnavailable: true),
            new MetricValue(current.EvToEbitda, HistoryUnavailable: true),
            new MetricValue(current.DividendYield, HistoryUnavailable: true));

        var impliedUpside = current.ConsensusTarget is { } target && current.Price is { } price && price > 0m
            ? decimal.Round((target / price - 1m) * PercentScale, 1)
            : (decimal?)null;

        var peerSet = await BuildPeerSetAsync(ticker, query.Peers, current.Sector, ct);

        await snapshots.AddAsync(new ValuationSnapshot
        {
            Ticker = ticker,
            Price = current.Price ?? 0m,
            TrailingPe = current.TrailingPe,
            ForwardPe = current.ForwardPe,
            EvToEbitda = current.EvToEbitda,
            DividendYield = current.DividendYield,
            ConsensusTarget = current.ConsensusTarget,
            IsStale = current.IsStale,
        }, ct);

        var sources = new List<string> { "yahoo:quoteSummary" };
        if (trailingHistory.FiveYearAvg is not null)
        {
            sources.Add("sec-edgar:xbrl");
        }

        return new ValuationSnapshotResult(
            ticker,
            NotApplicable: false,
            current.Price,
            current.IsStale,
            metrics,
            current.ConsensusTarget,
            impliedUpside,
            peerSet,
            sources,
            DateTimeOffset.UtcNow,
            current.Name,
            current.Sector,
            current.Industry,
            current.PriceAsOf,
            current.TargetLow,
            current.TargetHigh,
            current.AnalystCount,
            current.Currency,
            current.MarketCap);
    }

    private async Task<ValuationPeerSet?> BuildPeerSetAsync(
        string ticker, IReadOnlyList<string>? requested, string? sector, CancellationToken ct)
    {
        var isCustom = requested is { Count: > 0 };
        var symbols = isCustom
            ? requested!
            : await valuation.GetPeerSymbolsAsync(ticker, ct);

        symbols = symbols
            .Select(s => s.Trim().ToUpperInvariant())
            .Where(s => s.Length > 0 && s != ticker)
            .Distinct()
            .Take(MaxPeers)
            .ToList();

        if (symbols.Count == 0)
        {
            return null;
        }

        var peers = new List<ValuationPeer>();
        foreach (var symbol in symbols)
        {
            var metrics = await valuation.GetCurrentMetricsAsync(symbol, ct);
            peers.Add(new ValuationPeer(symbol, metrics?.ForwardPe, metrics?.EvToEbitda));
        }

        var name = isCustom
            ? "custom"
            : string.IsNullOrWhiteSpace(sector) ? "default" : $"sector:{sector} (default)";

        return new ValuationPeerSet(name, peers);
    }
}
