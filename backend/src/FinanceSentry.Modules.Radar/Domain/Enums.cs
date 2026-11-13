namespace FinanceSentry.Modules.Radar.Domain;

/// <summary>Kind of universe membership. (SignalSeverity is imported from Core.)</summary>
public enum UniverseKind
{
    Benchmark,
    Sector,
    Industry,
    Holding,
    Watchlist,

    /// <summary>
    /// A symbol the user carries an investment thesis on that is neither held nor watched. Without
    /// it the thesis monitor and the verdict would read bars no ingestion run refreshes.
    /// </summary>
    Thesis,

    /// <summary>
    /// A coverage-universe member ("our own index") that is neither held, watched nor under a thesis. The
    /// Ledger scoreboard grades coverage members from their stored bars, so they have to be ingested.
    /// </summary>
    Coverage,

    /// <summary>
    /// Broad-market index member the stage-1 scan shortlist picked up, carried for breadth rather
    /// than because it is owned or watched — so membership churns run to run as the shortlist does.
    /// The name persists as a string in <c>radar_universe.kind</c>; it stays <c>IndexConstituent</c>
    /// rather than tracking the stage-1 wording so existing rows keep parsing.
    /// </summary>
    IndexConstituent,
}

/// <summary>Where a universe member came from.</summary>
public enum UniverseSource
{
    Seed,
    Auto,
}

/// <summary>Subject type a signal is about.</summary>
public enum SignalSubjectType
{
    Ticker,
    Sector,
    Universe,
}

/// <summary>Whether the scanner records only (calibration) or also raises domain Alerts.</summary>
public enum ScannerMode
{
    LogOnly,
    Alerting,
}
