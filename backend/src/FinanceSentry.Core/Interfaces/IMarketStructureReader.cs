namespace FinanceSentry.Core.Interfaces;

/// <summary>
/// Core-facing read of 018 market structure — lets other modules (Research's opportunity scorer)
/// read a ticker's structure snapshot without depending on the Radar module directly.
/// </summary>
public interface IMarketStructureReader
{
    Task<MarketStructureSnapshot?> GetStructureAsync(string ticker, CancellationToken ct = default);

    /// <summary>
    /// Pairwise 63-day daily-return correlations among <paramref name="tickers"/>, computed from
    /// persisted bars. Pairs without enough overlapping history are omitted (022 FR-001d).
    /// </summary>
    Task<IReadOnlyList<PairwiseCorrelation>> GetPairwiseCorrelationsAsync(
        IReadOnlyCollection<string> tickers, CancellationToken ct = default);

    /// <summary>
    /// Structure snapshots for every active universe member (019 US2 scan input). Members whose
    /// bars are missing are omitted. <see cref="UniverseStructureEntry.IsEtfLens"/> marks
    /// benchmark/sector/industry ETFs — lenses, never auto-nominated as candidates.
    /// </summary>
    Task<IReadOnlyList<UniverseStructureEntry>> GetUniverseStructuresAsync(CancellationToken ct = default);
}

public sealed record PairwiseCorrelation(string TickerA, string TickerB, decimal Correlation);

public sealed record UniverseStructureEntry(string Ticker, bool IsEtfLens, MarketStructureSnapshot Snapshot);

/// <summary>
/// Projection of Radar's <c>TickerStructure</c> across the module boundary, plus the 019 FR-003
/// scoring inputs: the ticker's (affinity-assigned) sector rotation rank + delta, and distance
/// from the 63-day high (breakout state; 0 = at/above the high). Null when not computable. <see cref="BarCount"/> and
/// <see cref="LastBarDate"/> are the stored daily bars of the reader's lookback window and the date of the newest one (the
/// verdict-coverage audit, #1273, reports them so a thin or stale history is named, not guessed).
/// </summary>
public sealed record MarketStructureSnapshot(
    string Ticker,
    IReadOnlyDictionary<int, decimal?> RsByWindow,
    IReadOnlyDictionary<int, decimal?> ReturnByWindow,
    decimal? ExtensionFromMa50,
    decimal? TodayZScore,
    decimal? VolumeRatio,
    decimal? Ma50,
    decimal? Ma200,
    bool Stale,
    int? SectorRank = null,
    int? SectorRankDelta = null,
    decimal? DistanceFrom63dHigh = null,
    int BarCount = 0,
    DateOnly? LastBarDate = null);
