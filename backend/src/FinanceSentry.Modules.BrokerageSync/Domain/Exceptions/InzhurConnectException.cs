using FinanceSentry.Core.Exceptions;

namespace FinanceSentry.Modules.BrokerageSync.Domain.Exceptions;

/// <summary>A sign-in to the Inzhur cabinet could not go ahead; <see cref="ApiException.ErrorCode"/> is one of <see cref="InzhurErrorCodes"/>.</summary>
public sealed class InzhurConnectException(int statusCode, string errorCode, string message)
    : ApiException(statusCode, errorCode, message);

/// <summary>Error codes the Inzhur connect flow answers with; each has a message in the frontend error registry.</summary>
public static class InzhurErrorCodes
{
    /// <summary>No saved phone/password and none typed.</summary>
    public const string CredentialsRequired = "INZHUR_CREDENTIALS_REQUIRED";

    /// <summary>Today's sign-in attempts are used up (each may send an SMS).</summary>
    public const string LoginLimit = "INZHUR_LOGIN_LIMIT";

    /// <summary>Inzhur's reCAPTCHA check rejected the sign-in.</summary>
    public const string RecaptchaRejected = "INZHUR_RECAPTCHA_REJECTED";

    /// <summary>Inzhur rejected the phone/password.</summary>
    public const string InvalidCredentials = "INZHUR_INVALID_CREDENTIALS";

    /// <summary>The SMS step is gone: the code expired, or no sign-in is waiting for one.</summary>
    public const string ChallengeExpired = "INZHUR_CHALLENGE_EXPIRED";

    /// <summary>The code was wrong too many times; a new sign-in (and SMS) is needed.</summary>
    public const string TooManyAttempts = "INZHUR_TOO_MANY_ATTEMPTS";

    /// <summary>The sign-in browser or Inzhur itself is unreachable right now.</summary>
    public const string LoginUnavailable = "INZHUR_LOGIN_UNAVAILABLE";

    /// <summary>Inzhur answered in a way the sign-in does not recognise.</summary>
    public const string LoginFailed = "INZHUR_LOGIN_FAILED";

    /// <summary>No Inzhur connection exists for the caller.</summary>
    public const string NotConnected = "INZHUR_NOT_CONNECTED";
}
