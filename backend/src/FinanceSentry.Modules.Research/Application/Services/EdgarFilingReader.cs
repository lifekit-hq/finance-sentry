namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Modules.Research.Domain.Ports;

/// <summary><see cref="IEdgarFilingReader"/> impl over Research's own <see cref="ISecEdgarService"/>.</summary>
public sealed class EdgarFilingReader(ISecEdgarService edgar) : IEdgarFilingReader
{
    public async Task<IReadOnlyList<EdgarFilingItem>> GetRecentAsync(
        string ticker, IReadOnlyCollection<string> formTypes, int limit, CancellationToken ct = default)
    {
        // Feature 615 - opt into EDGAR's provider-failure signal so a failed read throws
        // EdgarProviderException instead of reading as "no filings".
        var filings = await edgar.GetRecentFilingsAsync(ticker, formTypes, limit, ct, surfaceProviderFailure: true);
        return filings
            .Select(f => new EdgarFilingItem(f.Form, f.FilingDate, f.ReportDate))
            .ToList();
    }
}
