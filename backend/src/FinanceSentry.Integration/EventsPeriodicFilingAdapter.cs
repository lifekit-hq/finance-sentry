namespace FinanceSentry.Integration;

using FinanceSentry.Modules.Events.Domain;
using FinanceSentry.Modules.Events.Domain.Exceptions;
using FinanceSentry.Modules.Events.Domain.Ports;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;

/// <summary>
/// Feature 049 - <see cref="IPeriodicFilingReader"/> over Research's EDGAR service: the ticker's
/// recent 10-K / 10-Q filings that carry a period end. A filing without a report date cannot anchor
/// a due date and is dropped here. Feature 615 - opts into EDGAR's provider-failure signal and
/// translates it to the Events-owned <see cref="FilingReadFailedException"/>, so the Events module
/// never references a Research type directly.
/// </summary>
public sealed class EventsPeriodicFilingAdapter(ISecEdgarService edgar) : IPeriodicFilingReader
{
    private static readonly string[] PeriodicForms = [FilingDueCalculator.AnnualForm, FilingDueCalculator.QuarterlyForm];
    private const int RecentLimit = 8;

    public async Task<IReadOnlyList<PeriodicFiling>> GetRecentAsync(string ticker, CancellationToken ct = default)
    {
        IReadOnlyList<EdgarFiling> filings;
        try
        {
            filings = await edgar.GetRecentFilingsAsync(ticker, PeriodicForms, RecentLimit, ct, surfaceProviderFailure: true);
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
