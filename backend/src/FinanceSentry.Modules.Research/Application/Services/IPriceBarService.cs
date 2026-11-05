namespace FinanceSentry.Modules.Research.Application.Services;

/// <summary>
/// OHLCV bars for one Yahoo ticker at one interval over one source range (the asset dossier's interactive
/// price chart). A bar the source reports without all four prices is dropped, never filled. Empty
/// <see cref="PriceBarSeries.Bars"/> when the source has no series.
/// </summary>
public interface IPriceBarService
{
    Task<PriceBarSeries> GetAsync(string yahooTicker, string interval, string sourceRange, CancellationToken ct = default);
}

/// <summary>
/// One bar. <see cref="Time"/> is the bar's start in epoch seconds shifted by the exchange's UTC offset (its
/// wall-clock reads as UTC), so a daily bar's date is the exchange's own trading date whatever the time zone.
/// </summary>
public sealed record PriceBar(long Time, decimal Open, decimal High, decimal Low, decimal Close, long Volume)
{
    public DateOnly Date => DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(Time).UtcDateTime);
}

/// <summary>Bars oldest first with strictly increasing <see cref="PriceBar.Time"/>, priced in <see cref="Currency"/> (the quote currency; null when the source omits it).</summary>
public sealed record PriceBarSeries(string? Currency, string? TimeZone, IReadOnlyList<PriceBar> Bars)
{
    public static PriceBarSeries Empty { get; } = new(null, null, []);
}
