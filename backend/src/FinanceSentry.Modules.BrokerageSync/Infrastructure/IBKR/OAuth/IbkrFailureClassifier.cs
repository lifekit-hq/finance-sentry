using System.Net;
using System.Security.Cryptography;
using FinanceSentry.Core.Connections;
using FinanceSentry.Infrastructure.Http;
using FinanceSentry.Infrastructure.Observability.Hangfire;
using FinanceSentry.Modules.BrokerageSync.Domain.Exceptions;

namespace FinanceSentry.Modules.BrokerageSync.Infrastructure.IBKR.OAuth;

/// <summary>
/// Reads an IBKR Web API (OAuth) sync failure for the connection-health policy (report §5).
/// <list type="bullet">
/// <item>A 429 is rate limited: it counts like a transient failure and is never a credential signal.</item>
/// <item>5xx, timeouts and network failures are transient, and so is a 401 whose body says
/// "backend down": IBKR answers the token request that way during an outage.</item>
/// <item>Any other 401/403 is a suspect credential failure: IBKR sends it for reasons other than a dead
/// credential too, so the policy waits for a confirmation before asking the user to reconnect.</item>
/// <item>A stored credential we cannot decrypt or parse can only be fixed by connecting again: a definitive
/// credential failure.</item>
/// <item>Anything else is unknown, and counts like any other non-credential failure.</item>
/// </list>
/// </summary>
public sealed class IbkrFailureClassifier : IProviderFailureClassifier
{
    public const string ProviderName = "ibkr";

    private const string BackendDownMarker = "backend down";

    public string Provider => ProviderName;

    public ProviderFailure Classify(Exception exception, ProviderFailureContext context)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var code = exception is BrokerAuthException { UpstreamStatusCode: { } status }
            ? $"HTTP_{(int)status}"
            : exception.GetType().Name;

        // Throttling is its own class: it counts like a transient failure but is never read as a credential signal.
        if (exception is BrokerAuthException { UpstreamStatusCode: HttpStatusCode.TooManyRequests }
            or HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests })
            return ProviderFailure.RateLimited(code);

        if (IsTransient(exception))
            return ProviderFailure.Transient(code);

        return exception switch
        {
            BrokerAuthException { UpstreamStatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } =>
                ProviderFailure.CredentialSuspect(code),
            BrokerCredentialUnreadableException or CryptographicException => ProviderFailure.CredentialDefinitive(code),
            _ => ProviderFailure.Unknown(code),
        };
    }

    private static bool IsTransient(Exception ex)
    {
        if (JobFailureTransientClassifier.IsTransient(ex))
            return true;

        // BrokerAuthException always maps to the same 422 INVALID_CREDENTIALS API error regardless of
        // cause, so an upstream IBKR blip (5xx) is only distinguishable via the original status code.
        if (ex is BrokerAuthException { UpstreamStatusCode: { } status } && TransientHttpStatus.IsTransient(status))
            return true;

        return ex is BrokerAuthException { UpstreamStatusCode: HttpStatusCode.Unauthorized } auth
            && auth.Message.Contains(BackendDownMarker, StringComparison.OrdinalIgnoreCase);
    }
}
