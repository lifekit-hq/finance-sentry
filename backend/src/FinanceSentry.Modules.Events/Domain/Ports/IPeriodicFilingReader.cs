namespace FinanceSentry.Modules.Events.Domain.Ports;

/// <summary>Recent 10-K / 10-Q filings for a ticker with their period ends (adapter over EDGAR). Empty when the ticker is not an EDGAR filer.</summary>
public interface IPeriodicFilingReader
{
    Task<IReadOnlyList<PeriodicFiling>> GetRecentAsync(string ticker, CancellationToken ct = default);
}

/// <summary>
/// A periodic SEC filing (10-K or 10-Q) with the period it covers. Part of the
/// <see cref="IPeriodicFilingReader"/> contract, so it lives beside the port (#673).
/// </summary>
public sealed record PeriodicFiling(string Form, DateOnly FilingDate, DateOnly ReportDate);

/// <summary>The SEC form types <see cref="IPeriodicFilingReader"/> returns.</summary>
public static class PeriodicFilingForms
{
    public const string Annual = "10-K";
    public const string Quarterly = "10-Q";
}
