namespace FinanceSentry.Modules.Research.Domain.Ports;

/// <summary>
/// Published read port (#986): the headline of the Ledger scoreboard - Ledger's decided proposals graded as proposed
/// against the S&amp;P 500 - for cross-module readers such as Radar's weekly performance brief. Implemented inside
/// Research; the FinanceSentry.Integration adapter reaches Research only through this interface.
/// </summary>
public interface ILedgerScoreboardReader
{
    Task<LedgerScoreboardReading> GetAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>
/// Ledger as proposed: <see cref="Decided"/> proposals confirmed or rejected, <see cref="Scored"/> of them graded at the
/// horizon, their mean gross excess over the S&amp;P 500 in percentage points (null with none scored), and whether the
/// sample is still below <see cref="MinimumSampleSize"/>.
/// </summary>
public sealed record LedgerScoreboardReading(
    int Decided,
    int Scored,
    decimal? MeanExcessPct,
    bool TooFewToTell,
    int MinimumSampleSize);
