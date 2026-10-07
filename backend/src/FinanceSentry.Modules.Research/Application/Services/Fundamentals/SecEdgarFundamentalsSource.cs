namespace FinanceSentry.Modules.Research.Application.Services.Fundamentals;

using FinanceSentry.Modules.Research.Domain;

/// <summary>
/// SEC EDGAR XBRL as a chain source, one instance per taxonomy (#837): <c>sec-edgar-us-gaap</c> for
/// domestic 10-K/10-Q filers (and us-gaap 20-F filers), <c>sec-edgar-ifrs-full</c> for IFRS foreign
/// private issuers. Keeping the taxonomies as separate sources lets the chain's freshness rule pick
/// the current one for a filer that moved from us-gaap to IFRS, instead of stale us-gaap history
/// hiding the IFRS filings.
/// </summary>
public sealed class SecEdgarFundamentalsSource(ISecEdgarService edgar, string taxonomy) : IFundamentalsSource
{
    public string Name { get; } = taxonomy == FundamentalFact.IfrsTaxonomy
        ? FundamentalsSourceNames.SecEdgarIfrs
        : FundamentalsSourceNames.SecEdgarUsGaap;

    public Task<FundamentalsSourceResult> FetchAsync(string ticker, CancellationToken ct)
        => edgar.GetTaxonomyFundamentalsAsync(ticker, taxonomy, ct);
}
