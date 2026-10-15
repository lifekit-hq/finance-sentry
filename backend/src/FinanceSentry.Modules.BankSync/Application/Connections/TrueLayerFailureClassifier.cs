namespace FinanceSentry.Modules.BankSync.Application.Connections;

using FinanceSentry.Core.Connections;
using FinanceSentry.Modules.BankSync.Infrastructure.TrueLayer;

/// <summary>
/// Reads a failed TrueLayer call as a <see cref="ProviderFailure"/> (connection health, Option B, S2).
/// A rejected refresh (<c>invalid_grant</c>) or an expired or revoked consent is a definitive credential loss.
/// A bare 401/403 is definitive only once the consent's <see cref="TrueLayerConnection"/> expiry has passed;
/// before it, or with no expiry known, TrueLayer may send it for other reasons, so it is only suspect.
/// </summary>
public sealed class TrueLayerFailureClassifier : IProviderFailureClassifier
{
    public const string ProviderName = "truelayer";

    /// <summary>The code an expired or revoked consent is recorded with; the reconnect prompt keys on it.</summary>
    public const string ConsentLostCode = "ITEM_LOGIN_REQUIRED";

    /// <summary>The code a network or timeout failure is recorded with.</summary>
    public const string UnavailableCode = "TRUELAYER_UNAVAILABLE";

    private const int ServerErrorStatus = 500;
    private const int UnauthorizedStatus = 401;
    private const int ForbiddenStatus = 403;
    private const int NotFoundStatus = 404;
    private const int TooManyRequestsStatus = 429;

    private static readonly string[] ConsentLostMarkers = ["invalid_grant", "consent has expired", "has been revoked"];

    public string Provider => ProviderName;

    public ProviderFailure Classify(Exception exception, ProviderFailureContext context) =>
        Classify(exception, context, consentExpiresAt: null);

    /// <param name="consentExpiresAt">The connection's <c>ConnectionExpiresAt</c>, when known.</param>
    public ProviderFailure Classify(Exception exception, ProviderFailureContext context, DateTimeOffset? consentExpiresAt)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(context);

        if (exception is not TrueLayerException tl)
            return BankFailureShapes.ClassifyNonProvider(exception, UnavailableCode);

        // TrueLayer reports a dead consent as invalid_grant in the token endpoint's body, not as a status.
        if (ConsentLostMarkers.Any(marker => tl.Message.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            return ProviderFailure.CredentialDefinitive(ConsentLostCode);

        // An unreadable answer says nothing about the consent. Missing TrueLayer configuration is ours too, but it never
        // heals on its own, so it stays Unknown and is told like any other persistent failure.
        if (tl.ErrorCode == "TRUELAYER_PARSE_ERROR")
            return ProviderFailure.Internal(tl.ErrorCode);

        // Thrown with a 503, but no TrueLayer outage: it must not read as transient.
        if (tl.ErrorCode == "TRUELAYER_NOT_CONFIGURED")
            return ProviderFailure.Unknown(tl.ErrorCode);

        return tl.StatusCode switch
        {
            TooManyRequestsStatus => ProviderFailure.RateLimited(
                tl.ErrorCode, RetryAfterHint.NotBefore(context.At, tl.RetryAfter)),
            >= ServerErrorStatus => ProviderFailure.Transient(tl.ErrorCode),
            UnauthorizedStatus or ForbiddenStatus when consentExpiresAt is { } expiresAt && context.At >= expiresAt =>
                ProviderFailure.CredentialDefinitive(tl.ErrorCode),
            UnauthorizedStatus or ForbiddenStatus => ProviderFailure.CredentialSuspect(tl.ErrorCode),
            NotFoundStatus => ProviderFailure.ResourceGone(tl.ErrorCode),
            _ => ProviderFailure.Unknown(tl.ErrorCode),
        };
    }
}
