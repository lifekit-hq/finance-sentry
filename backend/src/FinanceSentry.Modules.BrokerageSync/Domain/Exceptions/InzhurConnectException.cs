using FinanceSentry.Core.Exceptions;

namespace FinanceSentry.Modules.BrokerageSync.Domain.Exceptions;

/// <summary>Connecting the Inzhur cabinet could not go ahead; <see cref="ApiException.ErrorCode"/> is one of <see cref="InzhurErrorCodes"/>.</summary>
public sealed class InzhurConnectException(int statusCode, string errorCode, string message)
    : ApiException(statusCode, errorCode, message);

/// <summary>Error codes the Inzhur connect flow answers with; each has a message in the frontend error registry.</summary>
public static class InzhurErrorCodes
{
    /// <summary>Nothing was pasted.</summary>
    public const string SessionRequired = "INZHUR_SESSION_REQUIRED";

    /// <summary>The paste is not a bare cookie value (separators, whitespace, or far too long).</summary>
    public const string SessionInvalid = "INZHUR_SESSION_INVALID";

    /// <summary>Inzhur's refresh rejected the pasted session: expired, signed out, or not the refresh cookie.</summary>
    public const string SessionRejected = "INZHUR_SESSION_REJECTED";

    /// <summary>Inzhur is unreachable or rate-limiting right now.</summary>
    public const string Unavailable = "INZHUR_UNAVAILABLE";

    /// <summary>Inzhur answered the session check in a way the client does not recognise.</summary>
    public const string ConnectFailed = "INZHUR_CONNECT_FAILED";

    /// <summary>No Inzhur connection exists for the caller.</summary>
    public const string NotConnected = "INZHUR_NOT_CONNECTED";
}
