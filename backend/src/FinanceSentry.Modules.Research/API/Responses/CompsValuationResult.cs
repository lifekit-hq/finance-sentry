namespace FinanceSentry.Modules.Research.API.Responses;

/// <summary>A peer that made it into the statistics. <see cref="IndustryMatch"/> is null when either industry is unknown.</summary>
public record CompsPeerUsed(string Ticker, string? Name, string? Industry, bool? IndustryMatch);

/// <summary>A peer left out of the whole comparison, with the typed reason.</summary>
public record CompsPeerDropped(string Ticker, string Reason);

/// <summary>The peer set behind the statistics: the same default (and override) as <c>get_valuation_snapshot</c>.</summary>
public record CompsPeerSet(string Name, IReadOnlyList<CompsPeerUsed> Used, IReadOnlyList<CompsPeerDropped> Dropped);

/// <summary>A peer left out of one multiple only (negative EBITDA or earnings, a P/E outlier, a missing field).</summary>
public record CompsExclusion(string Ticker, string Reason);

/// <summary>Per-share value the peer 25th percentile, median and 75th percentile would imply. Null = could not be bridged.</summary>
public record CompsImpliedValue(decimal? P25, decimal? Median, decimal? P75);

/// <summary>
/// One compared figure: the target's value, the peer distribution (null when fewer than three usable peers),
/// the target's percentile rank among them and, for a multiple, the per-share value it implies.
/// <see cref="NotEvaluable"/> carries <see cref="NotEvaluableReason"/>: <c>INSUFFICIENT_PEERS</c> (no statistics),
/// <c>TARGET_*</c> (the target's own figure is missing or negative, so rank and implied value are null while the
/// peer statistics stay) or <c>FINANCIAL_SECTOR</c>. Growth and margin are percentages; multiples are ratios.
/// </summary>
public record CompsMeasure(
    decimal? Target,
    bool NotEvaluable,
    string? NotEvaluableReason,
    int UsableCount,
    decimal? Min,
    decimal? P25,
    decimal? Median,
    decimal? P75,
    decimal? Max,
    decimal? PercentileRank,
    CompsImpliedValue? ImpliedPerShare,
    IReadOnlyList<CompsExclusion> Excluded);

/// <summary>
/// The multiples compared. <see cref="PriceToBook"/> is evaluated for banks and insurers only (null otherwise);
/// their EV multiples come back <c>FINANCIAL_SECTOR</c>-not-evaluable.
/// </summary>
public record CompsMultiples(
    CompsMeasure EvToEbitda,
    CompsMeasure EvToRevenue,
    CompsMeasure TrailingPe,
    CompsMeasure ForwardPe,
    CompsMeasure? PriceToBook);

/// <summary>
/// The short reply: where the target ranks (percentile among its peers, 0-100) on each multiple, on growth and
/// on margin, and the per-share value at the peer median. <see cref="ImpliedValueBasis"/> names the multiple
/// behind <see cref="ImpliedValueMedian"/> (EV/EBITDA first, then trailing P/E, EV/Revenue, P/B).
/// </summary>
public record CompsSummary(
    decimal? EvToEbitdaPercentile,
    decimal? EvToRevenuePercentile,
    decimal? TrailingPePercentile,
    decimal? PriceToBookPercentile,
    decimal? GrowthPercentile,
    decimal? MarginPercentile,
    string? ImpliedValueBasis,
    decimal? ImpliedValueMedian,
    decimal? ImpliedUpsidePct,
    decimal? Price);

/// <summary>
/// Trading-comps valuation for one equity ticker (#1269): its multiples against a peer set, with peer
/// quartiles and the per-share value they imply. Computed on read from Yahoo <c>quoteSummary</c>; nothing is
/// stored and nothing is zero-filled. <see cref="NotApplicable"/> = non-equity.
/// </summary>
public record CompsValuationResult(
    string Ticker,
    bool NotApplicable,
    string? Name,
    string? Sector,
    string? Industry,
    string? Currency,
    string? NotEvaluableReason,
    CompsPeerSet? PeerSet,
    CompsSummary? Summary,
    CompsMultiples? Multiples,
    CompsMeasure? Growth,
    CompsMeasure? Margin,
    IReadOnlyList<string> Flags,
    IReadOnlyList<string> Sources,
    DateTimeOffset RetrievedAt)
{
    /// <summary>An explicit not-applicable result for a non-equity ticker (crypto) — no fabricated values.</summary>
    public static CompsValuationResult ForNonEquity(string ticker) => Empty(ticker, notApplicable: true, reason: null);

    /// <summary>The source returned nothing for the ticker: no number, the reason typed.</summary>
    public static CompsValuationResult Unavailable(string ticker)
        => Empty(ticker, notApplicable: false, reason: Domain.Valuation.CompsReasons.DataUnavailable);

    private static CompsValuationResult Empty(string ticker, bool notApplicable, string? reason) => new(
        ticker.Trim().ToUpperInvariant(),
        notApplicable,
        Name: null,
        Sector: null,
        Industry: null,
        Currency: null,
        reason,
        PeerSet: null,
        Summary: null,
        Multiples: null,
        Growth: null,
        Margin: null,
        Flags: [],
        Sources: [],
        RetrievedAt: DateTimeOffset.UtcNow);
}
