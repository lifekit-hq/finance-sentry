namespace FinanceSentry.Modules.Research.API.Responses;

/// <summary>
/// The asset dossier's price chart for one range (GET /research/assets/{symbol}/chart). Prices are closes in
/// <see cref="Currency"/>; <see cref="AssetChartPoint.Benchmark"/> is the S&amp;P 500 (SPY) rebased to the
/// asset's first close so both lines start together. <see cref="Trades"/> are the user's ingested fills inside
/// the window, each in its own trade currency. <see cref="AverageCost"/> is the open average cost per unit in
/// <see cref="Currency"/>, null when it cannot be stated in that currency. Empty <see cref="Points"/> = no
/// price history from the source.
/// </summary>
public sealed record AssetChartResult(
    string Symbol,
    string Range,
    string? Currency,
    IReadOnlyList<AssetChartPoint> Points,
    string BenchmarkLabel,
    IReadOnlyList<AssetChartTrade> Trades,
    decimal? AverageCost,
    decimal? ChangePct,
    decimal? BenchmarkChangePct,
    string Source,
    DateTimeOffset RetrievedAt);

public sealed record AssetChartPoint(DateOnly Date, decimal Close, decimal? Benchmark);

/// <summary>One fill: <see cref="Side"/> is <c>buy</c> or <c>sell</c>; quantity unsigned; price native in <see cref="Currency"/>.</summary>
public sealed record AssetChartTrade(DateOnly Date, string Side, decimal Quantity, decimal Price, string Currency);
