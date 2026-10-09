namespace FinanceSentry.Modules.BrokerageSync.Infrastructure.Inzhur;

/// <summary>
/// Configuration for the Inzhur cabinet sync. The hosts are the ones the web cabinet itself talks to; there is no
/// public API, so nothing here is a contract Inzhur has published (they said they will announce one when it exists).
/// </summary>
public sealed class InzhurOptions
{
    public const string SectionName = "Inzhur";

    /// <summary>Auth gateway: the session refresh. Its host is the domain of the refresh cookie the owner pastes.</summary>
    public string AuthBaseUrl { get; set; } = "https://api.inzhur.reit/auth";

    /// <summary>Core gateway: the read-only portfolio endpoints.</summary>
    public string CoreBaseUrl { get; set; } = "https://api.inzhur.reit/core";

    /// <summary>The cabinet's origin, sent as <c>Origin</c> on the cookie-bound refresh the way the cabinet sends it.</summary>
    public string CabinetOrigin { get; set; } = "https://www.inzhur.reit";

    /// <summary>
    /// The cookie the auth host keeps the session's refresh token in (HttpOnly, on <see cref="AuthBaseUrl"/>'s host).
    /// The owner copies its value from his browser after signing in on inzhur.reit.
    /// </summary>
    public string RefreshCookieName { get; set; } = "refreshToken";

    /// <summary>Honest user agent for the daily refresh and reads: who is calling and why.</summary>
    public string UserAgent { get; set; } = "FinanceSentry/1.0 (personal sync by the account holder)";

    /// <summary>Daily sync schedule (UTC): 07:00 is 10:00 in Kyiv, inside Inzhur's working day.</summary>
    public string SyncCron { get; set; } = "0 7 * * *";
}
