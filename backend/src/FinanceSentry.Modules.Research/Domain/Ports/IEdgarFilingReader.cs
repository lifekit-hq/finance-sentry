namespace FinanceSentry.Modules.Research.Domain.Ports;

/// <summary>
/// Published read port (#673): a ticker's recent SEC EDGAR filings, for cross-module readers such
/// as the Events module. Implemented inside Research; the FinanceSentry.Integration adapter reaches
/// Research only through this interface.
/// </summary>
public interface IEdgarFilingReader
{
    /// <summary>
    /// The ticker's most recent filings of <paramref name="formTypes"/>, newest first, at most
    /// <paramref name="limit"/>. A ticker that is not an EDGAR filer yields an empty list; a provider
    /// failure throws <see cref="EdgarProviderException"/> rather than returning an empty list.
    /// </summary>
    Task<IReadOnlyList<EdgarFilingItem>> GetRecentAsync(
        string ticker, IReadOnlyCollection<string> formTypes, int limit, CancellationToken ct = default);
}

/// <summary>One EDGAR filing as a cross-module reader sees it. <c>ReportDate</c> is the period end, when EDGAR carries one.</summary>
public sealed record EdgarFilingItem(
    string Form,
    DateOnly FilingDate,
    DateOnly? ReportDate);
