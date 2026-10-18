namespace FinanceSentry.Integration;

using FinanceSentry.Modules.Radar.Domain.Ports;
using FinanceSentry.Modules.Research.Domain.Ports;

/// <summary>
/// #986 - implements the Radar module's <see cref="ILedgerScoreboardSource"/> by reading Research's published
/// <see cref="ILedgerScoreboardReader"/> port, so Modules.Radar never references Modules.Research directly.
/// </summary>
public sealed class ResearchLedgerScoreboardSource(ILedgerScoreboardReader scoreboard) : ILedgerScoreboardSource
{
    public async Task<LedgerScoreboardLine?> GetAsync(Guid userId, CancellationToken ct = default)
    {
        var reading = await scoreboard.GetAsync(userId, ct);
        if (reading.Decided == 0)
        {
            return null;
        }

        return new LedgerScoreboardLine(
            reading.Scored,
            reading.Decided,
            reading.MeanExcessPct is decimal mean ? Math.Round(mean, 1) : null,
            reading.TooFewToTell,
            reading.MinimumSampleSize);
    }
}
