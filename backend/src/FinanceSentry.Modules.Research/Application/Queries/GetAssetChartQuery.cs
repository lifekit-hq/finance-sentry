namespace FinanceSentry.Modules.Research.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Dossier;

/// <summary>
/// The asset dossier's chart for one range: closes, the S&amp;P 500 rebased onto them, the user's fills inside
/// the window and the average-cost line. <paramref name="Range"/> must already be a <see cref="ChartRange"/>.
/// </summary>
public sealed record GetAssetChartQuery(Guid UserId, string Symbol, string Range) : IQuery<AssetChartResult>;

public sealed class GetAssetChartQueryHandler(
    IBookFiguresService bookFigures,
    IPriceHistoryService priceHistory,
    PositionReturnsReader returnsReader)
    : IQueryHandler<GetAssetChartQuery, AssetChartResult>
{
    private const string Source = "yahoo:chart";
    private const string Usd = "USD";

    public async Task<AssetChartResult> Handle(GetAssetChartQuery request, CancellationToken ct)
    {
        var symbol = request.Symbol.Trim().ToUpperInvariant();
        var range = ChartRange.Normalize(request.Range) ?? ChartRange.OneYear;

        var book = await bookFigures.ReadAsync(request.UserId, ct);
        var position = book.Positions.FirstOrDefault(p => p.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));

        var history = await priceHistory.GetAsync(DossierTickers.ToChartTicker(symbol, position?.AssetClass), range, ct);
        var benchmark = await priceHistory.GetAsync(DossierTickers.Benchmark, range, ct);

        var points = Rebase(history, benchmark);
        var windowStart = points.Count > 0 ? points[0].Date : DateOnly.MaxValue;

        IReadOnlyList<AssetChartTrade> trades = [];
        decimal? averageCost = null;
        if (position is not null)
        {
            var reading = await returnsReader.ReadAsync(request.UserId, position, ct);
            trades = reading.Ledger.Fills
                .Where(f => DateOnly.FromDateTime(f.TradedAt) >= windowStart)
                .Select(f => new AssetChartTrade(
                    DateOnly.FromDateTime(f.TradedAt),
                    f.Quantity > 0m ? "buy" : "sell",
                    Math.Abs(f.Quantity),
                    f.Price,
                    f.Currency.ToUpperInvariant()))
                .ToList();
            averageCost = AverageCost(reading.Returns.AverageCostNative, reading.Returns.CostCurrency, position, history.Currency);
        }

        var closes = points.Select(p => p.Close).ToList();
        var benchmarkCloses = points.Where(p => p.Benchmark is not null).Select(p => p.Benchmark!.Value).ToList();

        return new AssetChartResult(
            symbol,
            range,
            history.Currency,
            points,
            DossierTickers.BenchmarkLabel,
            trades,
            averageCost,
            PerformanceMath.ChangePct(closes),
            PerformanceMath.ChangePct(benchmarkCloses),
            Source,
            DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// The asset's closes with the benchmark's close on (or carried forward to) each date, rebased so the
    /// benchmark starts at the asset's first close. A crypto series trades on weekends; the benchmark does not.
    /// </summary>
    public static IReadOnlyList<AssetChartPoint> Rebase(PriceHistory asset, PriceHistory benchmark)
    {
        if (asset.Closes.Count == 0)
        {
            return [];
        }

        var benchmarkByDate = benchmark.Closes.OrderBy(c => c.Date).ToList();
        var points = new List<AssetChartPoint>(asset.Closes.Count);
        decimal? baseBenchmark = null;
        var firstClose = asset.Closes[0].Close;
        var cursor = -1;

        foreach (var close in asset.Closes)
        {
            while (cursor + 1 < benchmarkByDate.Count && benchmarkByDate[cursor + 1].Date <= close.Date)
            {
                cursor++;
            }

            decimal? rebased = null;
            if (cursor >= 0)
            {
                var raw = benchmarkByDate[cursor].Close;
                baseBenchmark ??= raw;
                rebased = baseBenchmark > 0m ? PriceRounding.Round(firstClose * raw / baseBenchmark.Value) : null;
            }

            points.Add(new AssetChartPoint(close.Date, close.Close, rebased));
        }

        return points;
    }

    private static decimal? AverageCost(
        decimal? averageCostNative, string? costCurrency, BookFigurePosition position, string? chartCurrency)
    {
        if (averageCostNative is decimal native && costCurrency is not null
            && costCurrency.Equals(chartCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return native;
        }

        // The provider's own cost basis is in USD, so it can only be drawn on a USD-quoted chart.
        return position.CostBasisUsd is decimal basis && position.Quantity > 0m
            && Usd.Equals(chartCurrency, StringComparison.OrdinalIgnoreCase)
                ? PriceRounding.Round(basis / position.Quantity)
                : null;
    }
}
