namespace FinanceSentry.Modules.Research.Domain.Ports;

// Opt-in failure signal for ISecEdgarService.GetRecentFilingsAsync: thrown only when the caller
// passes surfaceProviderFailure: true and the failure is a genuine EDGAR outage (the ticker->CIK
// map could not be loaded, or the submissions feed fetch failed) rather than the ticker simply not
// being a US-listed filer. Every other caller keeps the default swallow-and-return-empty behaviour
// untouched. ErrorCode (EdgarErrorCodes) says which of the two it was. It lives with Research's
// published read ports because IEdgarFilingReader lets it cross the module boundary as its failure
// signal (#673).
public sealed class EdgarProviderException(string message, string errorCode = EdgarErrorCodes.SourceUnavailable)
    : Exception(message)
{
    public string ErrorCode { get; } = errorCode;
}
