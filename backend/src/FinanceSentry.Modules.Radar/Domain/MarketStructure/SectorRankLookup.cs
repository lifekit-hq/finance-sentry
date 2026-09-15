namespace FinanceSentry.Modules.Radar.Domain.MarketStructure;

/// <summary>
/// The rotation table plus the sector ETF closes it ranks, loaded once and then asked for a ticker's
/// sector rank without further I/O. Rotation and affinity are universe-wide facts, so recomputing
/// them per ticker cost ~24 bar reads a member — unaffordable once the universe carries the S&amp;P 500
/// (#558). A sector ETF ranks as itself; anything else maps via return-correlation affinity.
/// </summary>
public sealed class SectorRankLookup(
    IReadOnlyList<SectorRotationRow> rotationRows,
    IReadOnlySet<string> sectorTickers,
    IReadOnlyDictionary<string, IReadOnlyDictionary<DateOnly, decimal>> sectorCloses)
{
    public (int? Rank, int? RankDelta) RankFor(string ticker, IReadOnlyList<DailyBar> series)
    {
        if (rotationRows.Count == 0)
        {
            return (null, null);
        }

        var upper = ticker.Trim().ToUpperInvariant();
        var sector = sectorTickers.Contains(upper)
            ? upper
            : BestAffinitySector(series);
        if (sector is null)
        {
            return (null, null);
        }

        var row = rotationRows.FirstOrDefault(r => string.Equals(r.Sector, sector, StringComparison.OrdinalIgnoreCase));
        return row is null ? (null, null) : (row.Rank, row.RankDelta);
    }

    private string? BestAffinitySector(IReadOnlyList<DailyBar> series)
        => series.Count == 0
            ? null
            : SectorAffinity.BestSector(series.ToDictionary(b => b.Date, b => b.AdjClose), sectorCloses);
}
