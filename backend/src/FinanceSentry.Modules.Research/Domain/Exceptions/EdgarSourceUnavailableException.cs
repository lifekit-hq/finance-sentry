namespace FinanceSentry.Modules.Research.Domain.Exceptions;

using FinanceSentry.Core.Exceptions;
using FinanceSentry.Modules.Research.Domain.Ports;

/// <summary>
/// A filings read that failed because EDGAR could not be used, as the API and MCP callers see it (#1375):
/// a 503 whose error code is <c>map_unavailable</c> (the ticker→CIK map could not be loaded) or
/// <c>source_unavailable</c> (the feed failed), never an empty list a caller would read as "no filings".
/// </summary>
public class EdgarSourceUnavailableException(EdgarProviderException cause)
    : ApiException(503, cause.ErrorCode, $"[{cause.ErrorCode}] {cause.Message} Retry later; this is not 'no filings'.", cause);
