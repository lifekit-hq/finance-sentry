namespace FinanceSentry.Modules.Wealth.Domain;

using FinanceSentry.Core.Headlines;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;

/// <summary>
/// One source of a stored <see cref="NetWorthSnapshot"/>, in the one shape every reader of the snapshot uses.
/// <see cref="Total"/> is the figure the snapshot counted for the source (in the snapshot's currency): for a sleeve
/// the snapshot carried forward, that is the carried figure its column holds, not what the source read this run.
/// Null only for a source outside the three sleeve columns whose read failed.
/// </summary>
public sealed record SnapshotSourceFigure(
    string SourceId,
    EconomyKind Kind,
    decimal? Total,
    SourceHealth Health,
    IReadOnlyList<string> Reasons)
{
    /// <summary>The source failed to read, or the snapshot carried it forward from an earlier day.</summary>
    public bool IsStale => Reasons.Contains(SourceReasons.StaleSource);
}

/// <summary>
/// The per-source view of a stored snapshot (framework §5.6, step 3), and the single adapter between the two
/// generations of stored data: a snapshot taken since per-source rows were written (<c>net_worth_snapshot_sources</c>)
/// reads its sources' health and reasons from those rows; an older snapshot, which has none, reads the three sleeve
/// columns and <see cref="NetWorthSnapshot.StaleSleeves"/> exactly as before - a sleeve listed there is DEGRADED with
/// <c>STALE_SOURCE</c>, anything else COMPLETE (those snapshots recorded nothing else per sleeve: their conversion
/// status and reconstruction flag stay on the snapshot). Readers derive stale state from the sources here, never by
/// parsing the column.
/// </summary>
public sealed class SnapshotSources
{
    /// <summary>The sources the snapshot's three sleeve columns hold, in column order.</summary>
    public static IReadOnlyList<string> SleeveIds { get; } = ["banking", "brokerage", "crypto"];

    private SnapshotSources(IReadOnlyList<SnapshotSourceFigure> sources) => Sources = sources;

    /// <summary>The three sleeves in column order, then any other source by id.</summary>
    public IReadOnlyList<SnapshotSourceFigure> Sources { get; }

    /// <summary>The ids of the sources that failed to read or were carried forward, in <see cref="Sources"/> order.</summary>
    public IReadOnlyList<string> StaleSources => [.. Sources.Where(s => s.IsStale).Select(s => s.SourceId)];

    /// <summary>
    /// The stale sleeves as the legacy comma-separated <see cref="NetWorthSnapshot.StaleSleeves"/> value
    /// (<c>banking,brokerage,crypto</c> order, null when none) - what the API and MCP have always returned.
    /// </summary>
    public string? StaleSleeves
    {
        get
        {
            var stale = Sources.Where(s => s.IsStale && SleeveIds.Contains(s.SourceId)).Select(s => s.SourceId).ToList();
            return stale.Count > 0 ? string.Join(',', stale) : null;
        }
    }

    public bool IsStale(string sourceId) => Sources.Any(s => s.SourceId == sourceId && s.IsStale);

    /// <summary>
    /// The snapshot's headline signals: <c>STALE_SOURCE</c> per stale source here, and the snapshot's own
    /// reconstruction flag and recorded conversion status (the rule <see cref="HeadlineSignals.FromSnapshot(string?, bool, FxStatus?)"/> applies).
    /// </summary>
    public HeadlineSignals Signals(NetWorthSnapshot snapshot)
        => HeadlineSignals.FromSnapshot(null, snapshot.IsApproximate, snapshot.FxStatus) with { StaleSources = StaleSources };

    /// <summary>
    /// The snapshot's sources from its per-source <paramref name="rows"/> (null or empty for a snapshot taken
    /// before they were written). A sleeve with no row of its own falls back to its column, so a snapshot always
    /// has its three sleeves.
    /// </summary>
    public static SnapshotSources Of(NetWorthSnapshot snapshot, IEnumerable<NetWorthSnapshotSource>? rows)
    {
        var byId = (rows ?? []).ToDictionary(r => r.SourceId, StringComparer.Ordinal);
        var legacyStale = (snapshot.StaleSleeves ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var sources = new List<SnapshotSourceFigure>(byId.Count + SleeveIds.Count);
        foreach (var sleeve in SleeveIds)
        {
            var column = SleeveColumn(snapshot, sleeve);
            if (byId.TryGetValue(sleeve, out var row))
            {
                var figure = FromRow(row);
                // A carried-forward or unread sleeve's row holds this run's reading (or nothing); the snapshot
                // counted the carried figure, which only its column holds.
                sources.Add(figure.IsStale || figure.Total is null ? figure with { Total = column } : figure);
            }
            else
            {
                var stale = legacyStale.Contains(sleeve, StringComparer.OrdinalIgnoreCase);
                sources.Add(new SnapshotSourceFigure(
                    sleeve,
                    SleeveKind(sleeve),
                    column,
                    stale ? SourceHealth.Degraded : SourceHealth.Complete,
                    stale ? [SourceReasons.StaleSource] : []));
            }
        }

        sources.AddRange(byId.Values
            .Where(r => !SleeveIds.Contains(r.SourceId))
            .OrderBy(r => r.SourceId, StringComparer.Ordinal)
            .Select(FromRow));

        return new SnapshotSources(sources);
    }

    /// <summary>
    /// <see cref="Of(NetWorthSnapshot, IEnumerable{NetWorthSnapshotSource}?)"/> for each of <paramref name="snapshots"/>,
    /// in their order, from the rows of all of them read at once.
    /// </summary>
    public static IReadOnlyList<(NetWorthSnapshot Snapshot, SnapshotSources Sources)> OfEach(
        IEnumerable<NetWorthSnapshot> snapshots, IEnumerable<NetWorthSnapshotSource>? rows)
    {
        var bySnapshot = (rows ?? []).ToLookup(r => r.SnapshotId);
        return [.. snapshots.Select(s => (s, Of(s, bySnapshot[s.Id])))];
    }

    private static SnapshotSourceFigure FromRow(NetWorthSnapshotSource row)
        => new(row.SourceId, row.Kind, row.Total, row.Health, row.Reasons);

    private static decimal SleeveColumn(NetWorthSnapshot snapshot, string sleeve) => sleeve switch
    {
        "banking" => snapshot.BankingTotal,
        "brokerage" => snapshot.BrokerageTotal,
        _ => snapshot.CryptoTotal,
    };

    // The kinds the three original wealth sources declare (WealthSources).
    private static EconomyKind SleeveKind(string sleeve) => sleeve switch
    {
        "banking" => EconomyKind.Bank,
        "brokerage" => EconomyKind.Brokerage,
        _ => EconomyKind.CryptoVenue,
    };
}
