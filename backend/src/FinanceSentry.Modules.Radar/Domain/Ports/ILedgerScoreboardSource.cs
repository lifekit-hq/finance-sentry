namespace FinanceSentry.Modules.Radar.Domain.Ports;

/// <summary>
/// Cross-module port (#986). Supplies the weekly brief with the Ledger scoreboard's headline - Ledger's decided
/// proposals graded as proposed against the S&amp;P 500 - so the digest carries one line on whether Ledger's own calls
/// beat the index. The adapter lives in FinanceSentry.Integration so Modules.Radar never depends on Modules.Research.
/// </summary>
public interface ILedgerScoreboardSource
{
    /// <summary>Null when the owner has decided no proposal yet, so the brief omits the line rather than print zeros.</summary>
    Task<LedgerScoreboardLine?> GetAsync(Guid userId, CancellationToken ct = default);
}

/// <param name="Scored">Decided proposals graded at the horizon - the sample size.</param>
/// <param name="Decided">Decided proposals in all, scored or not.</param>
/// <param name="MeanExcessPct">Mean excess over the S&amp;P 500 in percentage points, before costs; null with none scored.</param>
/// <param name="TooFewToTell">True below <paramref name="MinimumSampleSize"/> scored decisions.</param>
/// <param name="MinimumSampleSize">Scored decisions needed to tell skill from luck.</param>
public sealed record LedgerScoreboardLine(
    int Scored,
    int Decided,
    decimal? MeanExcessPct,
    bool TooFewToTell,
    int MinimumSampleSize);
