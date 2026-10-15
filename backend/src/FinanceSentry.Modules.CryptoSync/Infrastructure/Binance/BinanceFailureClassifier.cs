using System.Globalization;
using System.Net;
using FinanceSentry.Core.Connections;
using FinanceSentry.Infrastructure.Http;
using FinanceSentry.Infrastructure.Observability.Hangfire;
using FinanceSentry.Modules.CryptoSync.Domain;
using FinanceSentry.Modules.CryptoSync.Domain.Exceptions;

namespace FinanceSentry.Modules.CryptoSync.Infrastructure.Binance;

/// <summary>
/// Reads a Binance sync failure for the connection-health policy (report §5). A failed trade walk is read
/// by the venue failure inside it.
/// <list type="bullet">
/// <item>Binance's invalid-key codes say plainly the key is not honoured: a definitive credential failure.</item>
/// <item>A bare 401/403 is only suspect.</item>
/// <item>A 429 is rate limited: it counts like a transient failure and is never a credential signal.</item>
/// <item>5xx, timeouts and network failures are transient.</item>
/// <item>A stored key we cannot decrypt can only be fixed by entering it again: a definitive credential failure.</item>
/// <item>Anything else is unknown, and counts like any other non-credential failure.</item>
/// </list>
/// </summary>
public sealed class BinanceFailureClassifier : IProviderFailureClassifier
{
    // -1002 unauthorized, -1022 bad signature, -2008 unknown key id, -2014 malformed key,
    // -2015 rejected key / IP / permissions.
    public static readonly IReadOnlySet<int> CredentialErrorCodes = new HashSet<int> { -1002, -1022, -2008, -2014, -2015 };

    public string Provider => CryptoExchangeProvider.Binance;

    public ProviderFailure Classify(Exception exception, ProviderFailureContext context)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var cause = exception is CryptoTradeHistoryException { InnerException: { } inner } ? inner : exception;
        var code = cause switch
        {
            BinanceException { BinanceErrorCode: { } binanceCode } => binanceCode.ToString(CultureInfo.InvariantCulture),
            BinanceException { VenueStatusCode: { } status } => $"HTTP_{status}",
            _ => cause.GetType().Name,
        };

        // Throttling is its own class: it counts like a transient failure but is never read as a credential signal.
        if (cause is BinanceException { VenueStatusCode: (int)HttpStatusCode.TooManyRequests }
            or HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests })
            return ProviderFailure.RateLimited(code);

        if (JobFailureTransientClassifier.IsTransient(cause)
            || cause is BinanceException { VenueStatusCode: { } transientStatus }
                && TransientHttpStatus.IsTransient((HttpStatusCode)transientStatus))
            return ProviderFailure.Transient(code);

        return cause switch
        {
            BinanceException { BinanceErrorCode: { } errorCode } when CredentialErrorCodes.Contains(errorCode) =>
                ProviderFailure.CredentialDefinitive(code),
            BinanceException { VenueStatusCode: (int)HttpStatusCode.Unauthorized or (int)HttpStatusCode.Forbidden } =>
                ProviderFailure.CredentialSuspect(code),
            ExchangeCredentialUnreadableException => ProviderFailure.CredentialDefinitive(code),
            _ => ProviderFailure.Unknown(code),
        };
    }
}
