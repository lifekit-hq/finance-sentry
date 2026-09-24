namespace FinanceSentry.Modules.Events.Domain.Exceptions;

// Thrown by IPeriodicFilingReader.GetRecentAsync when a single ticker's EDGAR filing read failed
// at the provider (never when the ticker just isn't a filer). GetUpcomingEventsQueryHandler
// catches this per ticker so one bad ticker does not blank the whole filings source — it only
// marks the source unavailable when every requested ticker's read fails this way.
public sealed class FilingReadFailedException(string message, Exception? innerException = null)
    : Exception(message, innerException);
