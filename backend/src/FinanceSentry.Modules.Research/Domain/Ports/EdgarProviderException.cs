namespace FinanceSentry.Modules.Research.Domain.Ports;

// Opt-in failure signal for ISecEdgarService.GetRecentFilingsAsync: thrown only when the caller
// passes surfaceProviderFailure: true and the failure is a genuine EDGAR outage (the ticker->CIK
// map was never fetched successfully, or the submissions feed fetch failed) rather than the
// ticker simply not being a US-listed filer. Every other caller keeps the default
// swallow-and-return-empty behaviour untouched. It lives with Research's published read ports
// because IEdgarFilingReader lets it cross the module boundary as its failure signal (#673).
public sealed class EdgarProviderException(string message) : Exception(message);
