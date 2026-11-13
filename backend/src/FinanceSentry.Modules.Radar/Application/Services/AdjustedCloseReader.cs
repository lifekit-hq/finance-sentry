namespace FinanceSentry.Modules.Radar.Application.Services;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Radar.Domain.Repositories;

/// <summary><see cref="IAdjustedCloseReader"/> over the daily bars Radar already stores.</summary>
public sealed class AdjustedCloseReader(IDailyBarRepository bars) : IAdjustedCloseReader
{
    public async Task<IReadOnlyList<AdjustedClose>> GetSinceAsync(string ticker, DateOnly since, CancellationToken ct = default)
    {
        var series = await bars.GetSinceAsync(ticker.Trim().ToUpperInvariant(), since, ct);
        return [.. series.Select(b => new AdjustedClose(b.Date, b.AdjClose))];
    }
}
