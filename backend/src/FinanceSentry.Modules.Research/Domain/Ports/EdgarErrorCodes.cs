namespace FinanceSentry.Modules.Research.Domain.Ports;

/// <summary>
/// Machine-readable codes for an EDGAR failure (#1375), so a caller can tell an infrastructure
/// failure from a ticker that genuinely has no data. They ride <see cref="EdgarProviderException.ErrorCode"/>
/// and <c>FundamentalsCoverage.ErrorCode</c>.
/// </summary>
public static class EdgarErrorCodes
{
    /// <summary>The ticker→CIK map could not be loaded (or has been stale past its ceiling): no ticker can be resolved.</summary>
    public const string MapUnavailable = "map_unavailable";

    /// <summary>The ticker resolved but the EDGAR feed (submissions, concept facts) failed.</summary>
    public const string SourceUnavailable = "source_unavailable";
}
