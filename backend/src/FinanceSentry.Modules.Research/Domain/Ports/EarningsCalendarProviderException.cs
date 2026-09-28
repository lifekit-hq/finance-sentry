namespace FinanceSentry.Modules.Research.Domain.Ports;

// Opt-in failure signal for IEarningsCalendarService.GetForTickersAsync: thrown only when the
// caller passes surfaceProviderFailure: true and every requested ticker's fetch failed at the
// provider — a real Yahoo outage, not "no earnings scheduled". Every other caller keeps the
// default swallow-and-return-empty behaviour untouched. It lives with Research's published read
// ports because IEarningsCalendarReader lets it cross the module boundary unchanged (#673).
public sealed class EarningsCalendarProviderException(string message) : Exception(message);
