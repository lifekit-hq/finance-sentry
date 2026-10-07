namespace FinanceSentry.Modules.BrokerageSync.Infrastructure.Inzhur;

/// <summary>
/// Configuration for the Inzhur cabinet sync. The hosts are the ones the web cabinet itself talks to; there is no
/// public API, so nothing here is a contract Inzhur has published (they said they will announce one when it exists).
/// </summary>
public sealed class InzhurOptions
{
    public const string SectionName = "Inzhur";

    /// <summary>Auth gateway: login, 2FA verification and session refresh.</summary>
    public string AuthBaseUrl { get; set; } = "https://api.inzhur.reit/auth";

    /// <summary>Core gateway: the read-only portfolio endpoints.</summary>
    public string CoreBaseUrl { get; set; } = "https://api.inzhur.reit/core";

    /// <summary>The cabinet's own sign-in page; the login runs inside it so reCAPTCHA v3 scores a real page.</summary>
    public string SignInUrl { get; set; } = "https://www.inzhur.reit/dashboard/signin";

    /// <summary>The cabinet's origin, sent as <c>Origin</c> on the cookie-bound refresh the way the cabinet sends it.</summary>
    public string CabinetOrigin { get; set; } = "https://www.inzhur.reit";

    /// <summary>
    /// The headless Chromium sidecar's DevTools endpoint (<c>http://inzhur-browser:9222</c> in compose). Empty disables
    /// sign-in: the connect flow answers <c>INZHUR_LOGIN_UNAVAILABLE</c> and the daily sync keeps refreshing.
    /// </summary>
    public string BrowserUrl { get; set; } = string.Empty;

    /// <summary>Honest user agent for the daily refresh and reads: who is calling and why.</summary>
    public string UserAgent { get; set; } = "FinanceSentry/1.0 (personal sync by the account holder)";

    /// <summary>How long a started login waits for the SMS code (Inzhur's code lives 179 s).</summary>
    public TimeSpan PendingLoginLifetime { get; set; } = TimeSpan.FromMinutes(4);

    /// <summary>Upper bound for loading the sign-in page and running one login step in it.</summary>
    public TimeSpan BrowserStepTimeout { get; set; } = TimeSpan.FromSeconds(45);

    /// <summary>Daily sync schedule (UTC): 07:00 is 10:00 in Kyiv, inside Inzhur's working day.</summary>
    public string SyncCron { get; set; } = "0 7 * * *";
}
