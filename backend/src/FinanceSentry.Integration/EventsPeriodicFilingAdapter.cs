namespace FinanceSentry.Integration;

using FinanceSentry.Modules.Events.Domain.Ports;
using FinanceSentry.Modules.Research.Domain.Ports;

/// <summary>
/// Feature 049 - <see cref="IPeriodicFilingReader"/> over Research's published
/// <see cref="IEdgarFilingReader"/> port (#673): the ticker's recent 10-K / 10-Q filings that carry
/// a period end. A filing without a report date cannot anchor a due date and is dropped here.
/// Feature 615 - translates EDGAR's provider-failure signal to the Events-owned
/// <see cref="FilingReadFailedException"/>, so the Events module never references a Research type.
/// </summary>
public sealed class EventsPeriodicFilingAdapter(IEdgarFilingReader edgar) : IPeriodicFilingReader
{
    private static readonly string[] PeriodicForms = [PeriodicFilingForms.Annual, PeriodicFilingForms.Quarterly];
    private const int RecentLimit = 8;

    public async Task<IReadOnlyList<PeriodicFiling>> GetRecentAsync(string ticker, CancellationToken ct = default)
    {
        IReadOnlyList<EdgarFilingItem> filings;
        try
        {
            filings = await edgar.GetRecentAsync(ticker, PeriodicForms, RecentLimit, ct);
        }
        catch (EdgarProviderException ex)
        {
            throw new FilingReadFailedException($"EDGAR filing read failed for {ticker}.", ex);
        }

        return filings
            .Where(f => f.ReportDate is not null)
            .Select(f => new PeriodicFiling(f.Form, f.FilingDate, f.ReportDate!.Value))
            .ToList();
    }
}
