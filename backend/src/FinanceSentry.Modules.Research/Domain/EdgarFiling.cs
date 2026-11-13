namespace FinanceSentry.Modules.Research.Domain;

// A single SEC EDGAR filing. Fetched live from data.sec.gov; never persisted. Items are the 8-K item codes the submissions feed
// lists for the filing ("2.02", "9.01"); null for a form that carries none or when the feed did not say.
public sealed record EdgarFiling(
    string Ticker,
    string Form,
    DateOnly FilingDate,
    DateOnly? ReportDate,
    string Description,
    string AccessionNumber,
    string DocumentUrl,
    bool IsXbrl,
    IReadOnlyList<string>? Items = null);
