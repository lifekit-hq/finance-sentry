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
/// One bar. <see cref="Time"/> is the bar's real start in epoch seconds (unique and ascending down a series);
/// <see cref="Offset"/> is the exchange's UTC offset in seconds at that bar, so <c>Time + Offset</c> read as UTC is the
/// exchange's own wall-clock and <see cref="Date"/> its trading date, whatever the time zone.
/// </summary>
public sealed record PriceBar(long Time, long Offset, decimal Open, decimal High, decimal Low, decimal Close, long Volume)
{
    public DateOnly Date => DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(Time + Offset).UtcDateTime);
}

/// <summary>Bars oldest first by real instant, priced in <see cref="Currency"/> (the quote currency; null when the source omits it).</summary>
public sealed record PriceBarSeries(string? Currency, string? TimeZone, IReadOnlyList<PriceBar> Bars)
{
    public static PriceBarSeries Empty { get; } = new(null, null, []);
}
