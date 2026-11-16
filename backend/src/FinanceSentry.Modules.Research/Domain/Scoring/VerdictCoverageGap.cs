namespace FinanceSentry.Modules.Research.Domain.Scoring;

using System.Text.Json.Serialization;

/// <summary>
/// Why a section of the verdict-coverage audit (#1413) has no usable data: the closed set the audit computes in code,
/// never prose. A section with no gap is read; the audit's per-section <c>Detail</c> adds facts (never a reason). The
/// enum crosses the wire as its name. Plan #1273 section 2 is the source of the set.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<VerdictCoverageGap>))]
public enum VerdictCoverageGap
{
    /// <summary>The symbol has no row in the brokerage holdings feed (watched-only, crypto, or an Inzhur position).</summary>
    SourceMissing,

    /// <summary>A held position that is not an equity (<c>STK</c>) and is on no watchlist, thesis or coverage list, so the Radar universe leaves it out.</summary>
    OutsideUniverse,

    /// <summary>The data source answers nothing for the bare symbol: no bars, no valuation, or not in the SEC's ticker map. A non-US listing needs an exchange suffix the book does not carry.</summary>
    SymbolMapping,

    /// <summary>The source answered but too little: stale or short bars, a failed or empty fundamentals read, no price lens, too few peers.</summary>
    FeedLimit,

    /// <summary>The section does not apply to this kind of name (a fund's fundamentals, crypto valuation, a single name's look-through).</summary>
    NotApplicable,

    /// <summary>An ETF whose top holdings no source listed, so no look-through was applied.</summary>
    NoHoldings,

    /// <summary>An ETF look-through that read some of its top holdings but not all of them.</summary>
    Partial,

    /// <summary>The scorecard read threw: the bug bucket. The row's detail names the exception type.</summary>
    ScorecardThrew,

    /// <summary>A section's own read threw. The section's detail names the exception type.</summary>
    SectionThrew,
}
