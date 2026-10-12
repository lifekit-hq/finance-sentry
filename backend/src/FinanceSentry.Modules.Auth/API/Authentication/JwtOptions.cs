namespace FinanceSentry.Modules.Auth.API.Authentication;

/// <summary>
/// The <c>Jwt</c> configuration section: the HMAC key access tokens are signed and validated with, and the
/// lifetime of an app access token. Bound by the Auth module; the hosts that validate tokens (API, MCP over
/// HTTP) additionally require the secret at startup through <see cref="JwtOptionsValidator"/>.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>Lifetime of an app access token when <c>Jwt:ExpiryMinutes</c> is not set.</summary>
    public const int DefaultExpiryMinutes = 60;

    /// <summary>HMAC-SHA256 signing key (<c>Jwt:Secret</c>, env <c>Jwt__Secret</c>).</summary>
    public string? Secret { get; set; }

    /// <summary>Lifetime of an app access token, in minutes (<c>Jwt:ExpiryMinutes</c>).</summary>
    public int ExpiryMinutes { get; set; } = DefaultExpiryMinutes;

    public bool HasSecret => !string.IsNullOrWhiteSpace(Secret);
}
