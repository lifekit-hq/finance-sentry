namespace FinanceSentry.Modules.Events.Domain.Ports;

/// <summary>Recent 10-K / 10-Q filings for a ticker with their period ends (adapter over EDGAR). Empty when the ticker is not an EDGAR filer.</summary>
public interface IPeriodicFilingReader
{
    Task<IReadOnlyList<PeriodicFiling>> GetRecentAsync(string ticker, CancellationToken ct = default);
}
