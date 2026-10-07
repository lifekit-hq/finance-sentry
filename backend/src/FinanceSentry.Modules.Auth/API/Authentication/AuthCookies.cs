namespace FinanceSentry.Modules.Auth.API.Authentication;

using Microsoft.AspNetCore.Http;

/// <summary>
/// Names and options for the session cookies. When cookies are <c>Secure</c> (production), they are written
/// under the <c>__Host-</c> prefix, which makes the browser insist on Secure, <c>Path=/</c> and no Domain, so
/// no subdomain or plain-HTTP response can plant or overwrite them. Outside production (plain HTTP) the prefix
/// cannot be used and the unprefixed names are written.
/// <para>
/// Transition: production wrote the unprefixed names before the prefix was adopted. <see cref="Read"/> still
/// accepts them there, and every write deletes them, so nobody is signed out by the rename. The fallback
/// can go once the longest-lived legacy cookie has expired: the refresh token lives 30 days (the access
/// token only <c>Jwt:ExpiryMinutes</c>), so 30 days after the prefix reached production.
/// </para>
/// </summary>
public static class AuthCookies
{
    public const string AccessToken = "fs_access_token";
    public const string RefreshToken = "fs_refresh_token";

    /// <summary>The provider's ID token from sign-in, kept to hint which session to end at sign-out.</summary>
    public const string OidcIdToken = "fs_oidc_id_token";
    public const string HostPrefix = "__Host-";

    /// <summary>The name a cookie is written under.</summary>
    public static string NameFor(string baseName, bool secure) => secure ? HostPrefix + baseName : baseName;

    /// <summary>Reads a cookie, falling back to the legacy unprefixed name in production (see the transition note).</summary>
    public static string? Read(IRequestCookieCollection cookies, string baseName, bool secure)
    {
        if (!secure)
            return cookies[baseName];

        var value = cookies[HostPrefix + baseName];
        return string.IsNullOrWhiteSpace(value) ? cookies[baseName] : value;
    }

    /// <summary>Writes a cookie under its current name and drops the legacy unprefixed one.</summary>
    public static void Write(HttpResponse response, string baseName, string value, DateTimeOffset expires, bool secure)
    {
        response.Cookies.Append(NameFor(baseName, secure), value, Options(secure, expires));
        if (secure)
            response.Cookies.Delete(baseName, Options(secure));
    }

    /// <summary>Deletes a cookie under both its current and legacy names.</summary>
    public static void Delete(HttpResponse response, string baseName, bool secure)
    {
        response.Cookies.Delete(NameFor(baseName, secure), Options(secure));
        if (secure)
            response.Cookies.Delete(baseName, Options(secure));
    }

    private static CookieOptions Options(bool secure, DateTimeOffset? expires = null) => new()
    {
        HttpOnly = true,
        Secure = secure,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        Expires = expires,
    };
}
