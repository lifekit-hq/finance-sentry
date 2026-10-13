namespace FinanceSentry.Modules.Research.API.Responses;

/// <summary>
/// One valuation metric with its self-built history comparison. <see cref="Value"/> null =
/// unavailable, NEVER zero-filled (feature 030, FR-006). <see cref="HistoryUnavailable"/> flags
/// metrics for which no free historical series exists yet (EV/EBITDA, dividend yield) — those grow
/// their own window as snapshots accrue.
/// </summary>
public record MetricValue(
    decimal? Value,
    decimal? FiveYearAvg = null,
    int? HistoryWindowYears = null,
    bool HistoryUnavailable = false);

/// <summary>A peer in the comparison set with the two cross-sectional metrics we can source live.</summary>
public record ValuationPeer(string Ticker, decimal? ForwardPe, decimal? EvToEbitda);

/// <summary>The named peer set (default = sector/industry derived; overridable per request).</summary>
public record ValuationPeerSet(string Name, IReadOnlyList<ValuationPeer> Peers);

/// <summary>The four core valuation metrics, each with its own history comparison.</summary>
public record ValuationMetricsDto(
    MetricValue TrailingPe,
    MetricValue ForwardPe,
    MetricValue EvToEbitda,
    MetricValue DividendYield);

/// <summary>
/// A valuation snapshot for one ticker: current metrics vs the ticker's own history and a named peer
/// set, plus consensus target and implied upside. <see cref="NotApplicable"/> = non-equity (crypto)
/// — explicit, never fabricated (feature 030, FR-006). Every response also persists a
/// <c>valuation_snapshots</c> row so the comparison window grows organically. <see cref="Name"/>,
/// <see cref="Sector"/> and <see cref="Industry"/> are the company profile Yahoo already returns
/// with the metrics; <see cref="PriceAsOf"/> is when <see cref="Price"/> was last seen by the quote
/// path (null when unknown), so a stale price can say how old it is. <see cref="TargetLow"/>,
/// <see cref="TargetHigh"/> and <see cref="AnalystCount"/> describe the analyst target range behind
/// <see cref="ConsensusTarget"/> (the mean); <see cref="Currency"/> is the quote currency of every price.
/// </summary>
public record ValuationSnapshotResult(
    string Ticker,
    bool NotApplicable,
    decimal? Price,
    bool IsStale,
    ValuationMetricsDto Metrics,
    decimal? ConsensusTarget,
    decimal? ImpliedUpsidePct,
    ValuationPeerSet? PeerSet,
    IReadOnlyList<string> Sources,
    DateTimeOffset RetrievedAt,
    string? Name = null,
    string? Sector = null,
    string? Industry = null,
    DateTimeOffset? PriceAsOf = null,
    decimal? TargetLow = null,
    decimal? TargetHigh = null,
    int? AnalystCount = null,
    string? Currency = null,
    decimal? MarketCap = null)
{
    /// <summary>An explicit not-applicable result for a non-equity ticker (crypto) — no fabricated values.</summary>
    public static ValuationSnapshotResult ForNonEquity(string ticker) => new(
        ticker.Trim().ToUpperInvariant(),
        NotApplicable: true,
        Price: null,
        IsStale: false,
        new ValuationMetricsDto(new MetricValue(null), new MetricValue(null), new MetricValue(null), new MetricValue(null)),
        ConsensusTarget: null,
        ImpliedUpsidePct: null,
        PeerSet: null,
        Sources: [],
        RetrievedAt: DateTimeOffset.UtcNow);
}
