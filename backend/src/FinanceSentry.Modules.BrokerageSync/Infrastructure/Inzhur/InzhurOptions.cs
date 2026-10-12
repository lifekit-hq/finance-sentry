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

    /// <summary>
    /// How long the cabinet is assumed to keep a refresh token alive without a refresh. Measured from the stored
    /// session's metadata (2026-10-12): the refresh cookie's own Max-Age is 4000 h, re-issued by every refresh, and
    /// the access token's JWT lives 15 min. What the auth host enforces behind the cookie (an idle or absolute
    /// session limit) cannot be read from either, so the floor stays at the ~3 h the cabinet's own session cookie
    /// advertises (<c>sessionMaxAge: 10800</c>); raise it only on evidence from a session's logged age at reauth.
    /// </summary>
    public TimeSpan SessionLifetimeFloor { get; set; } = TimeSpan.FromHours(3);

    /// <summary>The keep-alive refreshes this many times within <see cref="SessionLifetimeFloor"/> (so a run can be missed twice).</summary>
    public int KeepAliveRunsPerLifetime { get; set; } = 3;

    /// <summary>The gap between keep-alive refreshes, derived from the lifetime floor (1 h at the defaults).</summary>
    public TimeSpan KeepAliveInterval => InzhurKeepAliveSchedule.IntervalFor(SessionLifetimeFloor, KeepAliveRunsPerLifetime);

    /// <summary>Keep-alive schedule (UTC), derived from <see cref="KeepAliveInterval"/>; never leaves a longer gap than it.</summary>
    public string KeepAliveCron => InzhurKeepAliveSchedule.CronFor(KeepAliveInterval);
}

/// <summary>
/// The cadence of the session keep-alive, kept apart from the daily holdings read: a refresh is what keeps the
/// cabinet's refresh chain alive, so it is sized from the session's lifetime, not from how often holdings are wanted.
/// </summary>
public static class InzhurKeepAliveSchedule
{
    /// <summary>Never refresh more often than this; the cabinet itself refreshes at most every access-token life (15 min).</summary>
    public static readonly TimeSpan MinInterval = TimeSpan.FromMinutes(15);

    /// <summary>Never wait longer than this, so a hand-set lifetime cannot turn the keep-alive into a daily run.</summary>
    public static readonly TimeSpan MaxInterval = TimeSpan.FromHours(12);

    /// <summary>Off the hour, away from the on-the-hour jobs of other providers.</summary>
    private const int MinuteOffset = 17;

    private const int MinutesPerHour = 60;

    public static TimeSpan IntervalFor(TimeSpan lifetime, int runsPerLifetime)
    {
        var interval = lifetime / Math.Max(1, runsPerLifetime);
        return interval < MinInterval ? MinInterval : interval > MaxInterval ? MaxInterval : interval;
    }

    /// <summary>A cron whose longest gap is at most <paramref name="interval"/> (a step that does not divide the day wraps shorter).</summary>
    public static string CronFor(TimeSpan interval)
    {
        var hours = (int)Math.Floor(interval.TotalHours);
        if (hours >= 1)
            return hours == 1 ? $"{MinuteOffset} * * * *" : $"{MinuteOffset} */{hours} * * *";

        var minutes = Math.Clamp((int)Math.Floor(interval.TotalMinutes), 1, MinutesPerHour - 1);
        return $"*/{minutes} * * * *";
    }
}
