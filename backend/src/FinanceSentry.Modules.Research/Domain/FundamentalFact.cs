namespace FinanceSentry.Modules.Research.Domain;

// A single reported financial datapoint, normalized to one shape whichever provider supplied it
// (#837: SEC EDGAR XBRL first, other sources only where EDGAR has nothing fresh). Fetched live and
// cached; never persisted. Value is in the given Unit (e.g. USD, USD/shares).
// Provenance: Form is the filing it came from (10-K, 10-Q, 20-F, 40-F, …/A; empty for a non-filing
// source), Taxonomy the XBRL taxonomy it was tagged in (us-gaap, ifrs-full) or the provider's own
// normalization, and SourceProvenance the provider, document and fetch time. A fact returned by the
// fundamentals chain always carries SourceProvenance.
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
    string Taxonomy = FundamentalFact.UsGaapTaxonomy,
    SourceProvenance? SourceProvenance = null)
{
    public const string UsGaapTaxonomy = "us-gaap";
    public const string IfrsTaxonomy = "ifrs-full";
}
