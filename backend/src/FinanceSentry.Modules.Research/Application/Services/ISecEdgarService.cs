namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Modules.Research.Application.Services.Fundamentals;
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
    // shareholders' equity) from EDGAR XBRL in ONE taxonomy — us-gaap, or ifrs-full for an IFRS
    // foreign private issuer filing 20-F/40-F — every periodic-statement datapoint, newest first, each
    // naming its form, taxonomy and provenance (the filing's EDGAR index). A ticker outside the SEC
    // map is NoData (not_sec_registrant); a fetch that failed on every tag is Failed, never NoData.
    // Read through the fundamentals chain (IFundamentalsService), which decides between taxonomies.
    Task<FundamentalsSourceResult> GetTaxonomyFundamentalsAsync(
        string ticker,
        string taxonomy,
        CancellationToken ct = default);
}
