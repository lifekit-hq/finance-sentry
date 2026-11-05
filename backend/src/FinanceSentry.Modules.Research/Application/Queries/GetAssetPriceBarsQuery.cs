namespace FinanceSentry.Modules.Research.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain.Exceptions;

/// <summary>
/// The dossier's interactive price chart: OHLCV bars for one period at one candle interval, with the 52-week
/// range. <paramref name="Period"/> and <paramref name="Interval"/> are validated here; a null interval is the
/// period's default.
/// </summary>
public sealed record GetAssetPriceBarsQuery(Guid UserId, string Symbol, string? Period, string? Interval)
    : IQuery<AssetPriceBarsResult>;

public sealed class GetAssetPriceBarsQueryHandler(
    IBookFiguresService bookFigures,
    IPriceBarService priceBars)
    : IQueryHandler<GetAssetPriceBarsQuery, AssetPriceBarsResult>
{
    private const string Source = "yahoo:chart";

    public async Task<AssetPriceBarsResult> Handle(GetAssetPriceBarsQuery request, CancellationToken ct)
    {
        var period = request.Period is null
            ? PricePeriod.OneYear
            : PricePeriod.Normalize(request.Period)
                ?? throw new InvalidPriceBarRequestException("period", request.Period, string.Join(", ", PricePeriod.Values));
        var allowed = PriceBarPlan.AllowedIntervals(period);
        var interval = request.Interval is null
            ? PriceBarPlan.DefaultInterval(period)
            : PriceInterval.Normalize(request.Interval) is { } named && allowed.Contains(named)
                ? named
                : throw new InvalidPriceBarRequestException(
                    "interval", request.Interval, $"{string.Join(", ", allowed)} for {period}");

        var symbol = request.Symbol.Trim().ToUpperInvariant();
        var book = await bookFigures.ReadAsync(request.UserId, ct);
        var position = book.Positions.FirstOrDefault(p => p.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));
        var ticker = DossierTickers.ToChartTicker(symbol, position?.AssetClass);

        var series = await priceBars.GetAsync(ticker, interval, PriceBarPlan.SourceRange(period, interval), ct);
        var daily = await priceBars.GetAsync(ticker, PriceInterval.OneDay, PriceBarPlan.WeekRangeSourceRange, ct);

        var (bars, windowStart, truncated) = PriceBarPlan.Trim(series.Bars, period, interval);
        var week = PriceBarPlan.WeekRange(daily.Bars);

        return new AssetPriceBarsResult(
            symbol,
            period,
            interval,
            allowed,
            series.Currency,
            series.TimeZone,
            bars.Select(b => new PriceBarDto(b.Time, b.Offset, b.Open, b.High, b.Low, b.Close, b.Volume)).ToList(),
            windowStart,
            truncated,
            week is { } w ? new WeekRangeDto(w.High, w.Low, w.From, w.CoversYear) : null,
            Source,
            DateTimeOffset.UtcNow);
    }
}
