namespace FinanceSentry.Core.Interfaces;

/// <summary>
/// An external data source's read of its own health (#1375): the SEC EDGAR ticker→CIK map today,
/// any lookup-table-backed provider tomorrow. Account providers report through
/// <c>IConnectionHealthReader</c>; this is the same visibility for the sources behind fundamentals,
/// filings and market data, so a source that is down surfaces at <c>get_sync_health</c> instead of
/// only as empty results downstream. Reads in-process state only - it never calls the provider.
/// </summary>
public interface IDataSourceHealthReader
{
    /// <summary>The source's current health.</summary>
    DataSourceHealth Read();
}

/// <summary>One external source's health.</summary>
/// <param name="Source">Stable source name (<c>sec-edgar</c>).</param>
/// <param name="State">One of <see cref="DataSourceHealthState"/>.</param>
/// <param name="LastSuccessAt">When the source last answered successfully; null when it never has.</param>
/// <param name="LastFailureAt">When it last failed; null when it never has.</param>
/// <param name="ConsecutiveFailures">Failed refreshes since the last success.</param>
/// <param name="Detail">What is wrong, or null when healthy.</param>
public sealed record DataSourceHealth(
    string Source,
    string State,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastFailureAt,
    int ConsecutiveFailures,
    string? Detail);

public static class DataSourceHealthState
{
    /// <summary>Nothing has asked the source yet in this process.</summary>
    public const string NeverChecked = "never_checked";

    /// <summary>The last refresh succeeded and the data is within its TTL.</summary>
    public const string Healthy = "healthy";

    /// <summary>The refresh is failing but a previously loaded copy is still being served.</summary>
    public const string Stale = "stale";

    /// <summary>The refresh is failing and there is nothing usable to serve: dependent reads fail.</summary>
    public const string Unavailable = "unavailable";
}
