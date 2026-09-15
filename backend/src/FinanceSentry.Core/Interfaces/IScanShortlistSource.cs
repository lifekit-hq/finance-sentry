namespace FinanceSentry.Core.Interfaces;

/// <summary>
/// Stage 1 of the opportunity funnel (#558): the bounded shortlist of broad-market tickers worth
/// paying per-ticker bar math for. Composed from cheap, market-wide signals only — no daily bars are
/// read to produce it — so consumers can widen their universe by tens of names instead of by a whole
/// index. Cross-module port: Research composes the shortlist, Radar scopes its ingestion to it.
/// </summary>
public interface IScanShortlistSource
{
    /// <summary>
    /// Shortlisted tickers, upper-cased and de-duplicated, best first. Empty when the pre-filter has
    /// no usable signal (an upstream outage degrades the universe to its core members rather than
    /// failing the run).
    /// </summary>
    Task<IReadOnlyList<string>> GetShortlistAsync(CancellationToken ct = default);
}
