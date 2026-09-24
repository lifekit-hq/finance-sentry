namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Modules.Research.Domain;

public interface ISecEdgarService
{
    // Recent SEC filings for a ticker, newest first, optionally restricted to given form types
    // (e.g. 10-K, 10-Q, 8-K). Returns empty when the ticker maps to no US-listed EDGAR filer, and
    // (by default) also when the CIK map or submissions fetch fails at the provider. Pass
    // surfaceProviderFailure: true to opt into EdgarProviderException for the latter case only —
    // "not a filer" still returns empty even then.
    Task<IReadOnlyList<EdgarFiling>> GetRecentFilingsAsync(
        string ticker,
        IReadOnlyCollection<string>? formTypes,
        int limit,
        CancellationToken ct = default,
        bool surfaceProviderFailure = false);

    // Key reported fundamentals (revenue, gross profit, net income, diluted EPS, operating income,
    // shareholders' equity) from EDGAR XBRL, newest first, up to maxPerConcept datapoints each.
    Task<IReadOnlyList<FundamentalFact>> GetFundamentalsAsync(
        string ticker,
        int maxPerConcept,
        CancellationToken ct = default);
}
