using System.Net;
using FinanceSentry.Core.Connections;
using FinanceSentry.Infrastructure.Observability.Hangfire;
using FinanceSentry.Modules.CryptoSync.Domain;
using FinanceSentry.Modules.CryptoSync.Domain.Exceptions;

namespace FinanceSentry.Modules.CryptoSync.Infrastructure.RevolutX;

/// <summary>
/// Reads a Revolut X sync failure for the connection-health policy (report §5). A failed trade walk is read
/// by the venue failure inside it.
/// <list type="bullet">
/// <item>A 401/403 is only suspect: Revolut X sends no code that says the key itself is gone.</item>
/// <item>A 429 is rate limited: it counts like a transient failure and is never a credential signal.</item>
/// <item>5xx, timeouts and network failures are transient (the HTTP failure rides as the inner exception).</item>
/// <item>A stored key we cannot decrypt, or a key pair that cannot be loaded, can only be fixed by entering it
/// again: a definitive credential failure.</item>
/// <item>Anything else is unknown, and counts like any other non-credential failure.</item>
/// </list>
/// </summary>
public sealed class RevolutXFailureClassifier : IProviderFailureClassifier
{
    public string Provider => CryptoExchangeProvider.RevolutX;

    public ProviderFailure Classify(Exception exception, ProviderFailureContext context)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var cause = exception is CryptoTradeHistoryException { InnerException: { } inner } ? inner : exception;
        var code = cause is RevolutXException { VenueStatusCode: { } status } ? $"HTTP_{status}" : cause.GetType().Name;

        // Throttling is its own class: it counts like a transient failure but is never read as a credential signal.
        if (cause is RevolutXException { VenueStatusCode: (int)HttpStatusCode.TooManyRequests }
            or HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests })
            return ProviderFailure.RateLimited(code);

        if (JobFailureTransientClassifier.IsTransient(cause))
            return ProviderFailure.Transient(code);

        return cause switch
        {
            RevolutXException { VenueStatusCode: (int)HttpStatusCode.Unauthorized or (int)HttpStatusCode.Forbidden } =>
                ProviderFailure.CredentialSuspect(code),
            RevolutXException { IsUnreadableKey: true } or ExchangeCredentialUnreadableException =>
                ProviderFailure.CredentialDefinitive(code),
            _ => ProviderFailure.Unknown(code),
        };
    }
}
