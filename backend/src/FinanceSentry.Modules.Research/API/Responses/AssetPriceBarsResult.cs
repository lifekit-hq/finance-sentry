namespace FinanceSentry.Modules.Research.API.Responses;

/// <summary>
/// The dossier's interactive price chart for one period and interval (GET /research/assets/{symbol}/bars).
/// <see cref="Bars"/> are the period's window plus up to 200 bars before it, so a moving average is defined from
/// <see cref="WindowStart"/>; times are real epoch seconds, each bar carrying the exchange's UTC offset (<see cref="TimeZone"/>) at that bar for display, prices in
/// <see cref="Currency"/>. <see cref="Truncated"/> is true when the asset's history starts after the period does (the
/// client says the history starts at <see cref="WindowStart"/>). <see cref="Intervals"/> are the candle intervals the
/// period can honestly offer. Empty <see cref="Bars"/> = no price history from the source.
/// </summary>
public sealed record AssetPriceBarsResult(
    string Symbol,
    string Period,
    string Interval,
    IReadOnlyList<string> Intervals,
    string? Currency,
    string? TimeZone,
    IReadOnlyList<PriceBarDto> Bars,
    long WindowStart,
    bool Truncated,
    WeekRangeDto? WeekRange,
    string Source,
    DateTimeOffset RetrievedAt);

/// <summary>One bar: <c>Time</c> the real start in epoch seconds (strictly ascending), <c>Offset</c> the exchange's UTC offset in seconds at that bar (<c>Time + Offset</c> read as UTC is its wall-clock).</summary>
public sealed record PriceBarDto(long Time, long Offset, decimal Open, decimal High, decimal Low, decimal Close, long Volume);

/// <summary>The highest high and lowest low of the 52 weeks to the last daily bar; <see cref="CoversYear"/> false = the series is younger, so it is the range since <see cref="From"/>.</summary>
public sealed record WeekRangeDto(decimal High, decimal Low, DateOnly From, bool CoversYear);
