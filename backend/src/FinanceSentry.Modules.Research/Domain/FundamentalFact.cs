namespace FinanceSentry.Modules.Research.Domain;

// A single reported financial datapoint from SEC EDGAR XBRL company facts.
// Fetched live; never persisted. Value is in the given Unit (e.g. USD, USD/shares).
// Provenance: Form is the filing it came from (10-K, 10-Q, 20-F, 40-F, …/A) and Taxonomy the XBRL
// taxonomy it was tagged in (us-gaap, or ifrs-full for an IFRS foreign private issuer).
public sealed record FundamentalFact(
    string Ticker,
    string Concept,
    string Label,
    string Unit,
    decimal Value,
    DateOnly PeriodEnd,
    string? FiscalPeriod,
    int? FiscalYear,
    string Form,
    string Taxonomy = FundamentalFact.UsGaapTaxonomy)
{
    public const string UsGaapTaxonomy = "us-gaap";
    public const string IfrsTaxonomy = "ifrs-full";
}
