namespace FinanceSentry.Modules.Radar.Domain;

/// <summary>
/// The invested book's concentration and allocation over the days it was captured, against the IPS targets.
/// Shares are fractions of the invested book (positions with a price; idle cash is not in the denominator of
/// <see cref="ConcentrationPoint.Top3Share"/> and <see cref="ConcentrationPoint.CryptoShare"/>). <see cref="Reason"/>
/// is set, and <see cref="Points"/> empty, when there is too little history to draw a line.
/// </summary>
public sealed record ConcentrationHistory(
    IReadOnlyList<ConcentrationPoint> Points,
    decimal? CryptoTarget,
    decimal? CashTarget,
    string? Reason);

/// <param name="Top3Share">The three largest positions' share; a symbol held on several venues counts once.</param>
/// <param name="CryptoShare">The crypto sleeve's share of the invested book.</param>
/// <param name="CashShare">Cash's share of the book's brokerage and crypto sources that day; null when no split was measured.</param>
public sealed record ConcentrationPoint(DateOnly Date, decimal Top3Share, decimal CryptoShare, decimal? CashShare);
