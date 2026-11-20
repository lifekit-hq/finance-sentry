namespace FinanceSentry.Modules.Radar.Domain;

/// <summary>A measured rate (a fraction) or the typed reason there is none; never a zero standing in for either.</summary>
public sealed record RiskFigure(decimal? Value, string? Reason);

/// <summary>
/// The risk picture of the brokerage book (framework M8) and of the whole book's concentration.
/// Volatility, maximum drawdown and beta come from the monthly time-weighted returns between
/// <paramref name="From"/> and <paramref name="To"/> (<paramref name="MonthsObserved"/> of them;
/// <paramref name="BetaMonths"/> shared with SPY). Weights are fractions of the whole book.
/// <paramref name="PositionFlag"/> is the stored single-position limit used and <paramref name="Top1OverFlag"/>
/// whether the largest position exceeds it (null when no flag is stored).
/// </summary>
public sealed record RiskPicture(
    DateOnly? From,
    DateOnly? To,
    int MonthsObserved,
    int BetaMonths,
    RiskFigure Volatility,
    RiskFigure MaxDrawdown,
    RiskFigure Beta,
    decimal? DrawdownTolerance,
    string? Top1Symbol,
    decimal? Top1Weight,
    IReadOnlyList<string> Top3Symbols,
    decimal? Top3Weight,
    decimal? CryptoShare,
    decimal? PositionFlag,
    bool? Top1OverFlag);
