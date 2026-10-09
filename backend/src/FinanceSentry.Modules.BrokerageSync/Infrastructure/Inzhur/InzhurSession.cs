using System.Net;
using System.Text.Json;

namespace FinanceSentry.Modules.BrokerageSync.Infrastructure.Inzhur;

/// <summary>
/// The cabinet session finance-sentry holds for one user: the short-lived bearer token and every cookie the auth host
/// set (the HttpOnly refresh cookie among them). Serialized to JSON and encrypted at rest as one secret; replaced whole
/// after every refresh, since the refresh cookie may rotate. Never logged — <see cref="ToString"/> is redacted.
/// </summary>
public sealed record InzhurSession(string AccessToken, IReadOnlyList<InzhurCookie> Cookies)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Serialize() => JsonSerializer.Serialize(this, JsonOptions);

    public static InzhurSession Deserialize(string json)
        => JsonSerializer.Deserialize<InzhurSession>(json, JsonOptions)
           ?? throw new InvalidOperationException("Stored Inzhur session is empty.");

    /// <summary>
    /// The session as the owner hands it over: only the auth host's refresh cookie, copied from his browser after a
    /// sign-in on inzhur.reit. No access token yet; the first refresh fetches one and completes the jar.
    /// </summary>
    public static InzhurSession FromRefreshCookie(string cookieName, string value, Uri authBaseUrl)
        => new(string.Empty, [new InzhurCookie(cookieName, value, authBaseUrl.Host, "/", null, Secure: true, HttpOnly: true)]);

    /// <summary>A cookie container holding the jar, for one sync's requests and the Set-Cookie headers they return.</summary>
    public CookieContainer ToContainer()
    {
        var container = new CookieContainer();
        foreach (var c in Cookies)
        {
            if (c.ExpiresUtc is { } expires && expires <= DateTime.UtcNow)
                continue;

            container.Add(new Cookie(c.Name, c.Value, c.Path, c.Domain)
            {
                Secure = c.Secure,
                HttpOnly = c.HttpOnly,
                Expires = c.ExpiresUtc ?? DateTime.MinValue,
            });
        }

        return container;
    }

    /// <summary>The jar as it stands after a sync: what the container now holds, with the newest access token.</summary>
    public static InzhurSession FromContainer(string accessToken, CookieContainer container)
        => new(accessToken, container.GetAllCookies()
            .Where(c => !c.Expired)
            .Select(c => new InzhurCookie(
                c.Name, c.Value, c.Domain, c.Path,
                c.Expires == DateTime.MinValue ? null : c.Expires.ToUniversalTime(),
                c.Secure, c.HttpOnly))
            .ToList());

    public override string ToString() => $"InzhurSession {{ Cookies = {Cookies.Count}, AccessToken = [redacted] }}";
}

/// <summary>One stored cookie. <see cref="ToString"/> is redacted so a logged jar never leaks a value.</summary>
public sealed record InzhurCookie(
    string Name,
    string Value,
    string Domain,
    string Path,
    DateTime? ExpiresUtc,
    bool Secure,
    bool HttpOnly)
{
    public override string ToString() => $"InzhurCookie {{ Name = {Name}, Domain = {Domain}, Value = [redacted] }}";
}
