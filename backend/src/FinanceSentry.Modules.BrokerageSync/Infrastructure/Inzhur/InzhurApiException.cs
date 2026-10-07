namespace FinanceSentry.Modules.BrokerageSync.Infrastructure.Inzhur;

public enum InzhurFailureKind
{
    /// <summary>The session is gone (refresh rejected, or a fresh token rejected): only the owner can sign in again.</summary>
    ReauthRequired,

    /// <summary>Inzhur asked to slow down (429).</summary>
    RateLimited,

    /// <summary>Inzhur or the network is failing (5xx, timeout, connection reset).</summary>
    Unavailable,

    /// <summary>Anything else: an unexpected status or a body that is not the shape the cabinet reads.</summary>
    Unexpected,
}

/// <summary>
/// A failed call to the cabinet. The message carries the status and Inzhur's own error text (never a token, cookie or
/// request body) so it is safe to log and to keep as the credential's last error.
/// </summary>
public sealed class InzhurApiException(InzhurFailureKind kind, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public InzhurFailureKind Kind { get; } = kind;

    public bool IsTransient => Kind is InzhurFailureKind.RateLimited or InzhurFailureKind.Unavailable;
}
