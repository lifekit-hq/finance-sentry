using System.Net;
using System.Security.Cryptography;
using FinanceSentry.Core.Connections;
using FinanceSentry.Infrastructure.Observability.Hangfire;
using FinanceSentry.Modules.BrokerageSync.Domain.Exceptions;

namespace FinanceSentry.Modules.BrokerageSync.Infrastructure.IBKR.Flex;

/// <summary>
/// Reads an IBKR Flex Web Service sync failure for the connection-health policy (report §5), by the
/// numeric code in IBKR's <c>FlexStatementResponse</c>:
/// <list type="bullet">
/// <item>An expired or invalid token, an IP restriction or an unknown query says plainly that only the user
/// can fix the connection: a definitive credential failure.</item>
/// <item>"Invalid request or unable to validate request" (1020) and a bare 401/403 are only suspect.</item>
/// <item>A statement still generating, data not ready yet, heavy load or throttling is transient, as are
/// 5xx, timeouts and network failures.</item>
/// <item>A stored token we cannot decrypt can only be fixed by entering it again: a definitive credential
/// failure.</item>
/// <item>Anything else is unknown, and counts like any other non-credential failure.</item>
/// </list>
/// IBKR's "statement is not available" answer (<see cref="IbkrFlexException.IsStatementNotAvailable"/>) reaches
/// here only once the client's retries and the scheduled run's one-day step-back are spent, and reads as unknown.
/// </summary>
public sealed class IbkrFlexFailureClassifier : IProviderFailureClassifier
{
    public const string ProviderName = "ibkr-flex";

    // 1012 token expired, 1013 IP restriction, 1014 query invalid, 1015 token invalid.
    private static readonly HashSet<string> CredentialErrorCodes = ["1012", "1013", "1014", "1015"];

    // 1020 invalid request or unable to validate request.
    private static readonly HashSet<string> SuspectCredentialErrorCodes = ["1020"];

    // 1001 could not be generated at this time, 1004–1008 incomplete / not ready yet,
    // 1009 heavy load, 1018 too many requests, 1019 generation in progress,
    // 1021 could not be retrieved at this time.
    private static readonly HashSet<string> TransientErrorCodes =
        ["1001", "1004", "1005", "1006", "1007", "1008", "1009", "1018", "1019", "1021"];

    public string Provider => ProviderName;

    public ProviderFailure Classify(Exception exception, ProviderFailureContext context)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is IbkrFlexException flex)
            return ClassifyFlex(flex);

        var code = exception.GetType().Name;
        if (JobFailureTransientClassifier.IsTransient(exception))
            return ProviderFailure.Transient(code);

        return exception is BrokerCredentialUnreadableException or CryptographicException
            ? ProviderFailure.CredentialDefinitive(code)
            : ProviderFailure.Unknown(code);
    }

    private static ProviderFailure ClassifyFlex(IbkrFlexException flex)
    {
        if (flex.FlexErrorCode is { } errorCode)
        {
            if (CredentialErrorCodes.Contains(errorCode))
                return ProviderFailure.CredentialDefinitive(errorCode);
            if (SuspectCredentialErrorCodes.Contains(errorCode))
                return ProviderFailure.CredentialSuspect(errorCode);
            return TransientErrorCodes.Contains(errorCode)
                ? ProviderFailure.Transient(errorCode)
                : ProviderFailure.Unknown(errorCode);
        }

        return flex.UpstreamStatusCode is { } status
            ? status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? ProviderFailure.CredentialSuspect($"HTTP_{(int)status}")
                : ProviderFailure.Unknown($"HTTP_{(int)status}")
            : ProviderFailure.Unknown(nameof(IbkrFlexException));
    }
}
