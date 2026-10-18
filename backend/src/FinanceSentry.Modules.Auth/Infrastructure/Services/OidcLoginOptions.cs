namespace FinanceSentry.Modules.Auth.Infrastructure.Services;

/// <summary>
/// The org identity provider (OIDC) sign-in, bound from <c>Auth:Oidc</c>. The whole login is off until
/// <see cref="Authority"/>, <see cref="ClientId"/>, <see cref="ClientSecret"/> and <see cref="PublicBaseUrl"/>
/// are all set (see <see cref="IsConfigured"/>); nothing is registered and the endpoints answer
/// <c>SIGN_IN_METHOD_DISABLED</c> until then.
/// </summary>
public class OidcLoginOptions
{
    public const string SectionName = "Auth:Oidc";

    /// <summary>The authentication scheme name, and the provider name stored in <c>AspNetUserLogins</c>.</summary>
    public const string Scheme = "lifekit";

    /// <summary>The path the provider returns the browser to; the OIDC handler completes the code exchange there.</summary>
    public const string CallbackPath = "/api/v1/auth/oidc/signin";

    /// <summary>The claim (and the scope that asks for it) carrying the names of the person's provider roles.</summary>
    public const string RolesClaim = "roles";

    /// <summary>The SPA page a refused sign-in lands on.</summary>
    public const string AccessDeniedPath = "/access-denied";

    /// <summary>The issuer, e.g. <c>https://host.tailnet.ts.net:3001/oidc</c>.</summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>
    /// Optional address the API itself uses for discovery, JWKS, token and userinfo calls, e.g.
    /// <c>http://logto:3001/oidc</c> on a shared docker network when a container cannot reach the provider's
    /// public (tailnet) address. The issuer validated and the browser's authorize redirect keep using
    /// <see cref="Authority"/>. Empty = call the authority directly.
    /// </summary>
    public string BackchannelAuthority { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    /// <summary>The confidential client's secret. Comes from the SOPS-rendered env, never from a committed file.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// The origin the browser reaches the app on (e.g. <c>https://host.tailnet.ts.net:4200</c>). The redirect
    /// URI registered with the provider and the post-login redirect are built from it: the API sits behind
    /// proxies that do not preserve the public scheme and port, so it cannot derive them from the request.
    /// </summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Turn off only for a local, plain-HTTP provider; otherwise keep HTTPS metadata. Ignored (treated as off) when
    /// <see cref="BackchannelAuthority"/> is plain HTTP, because the provider then advertises its JWKS on that address.
    /// </summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>
    /// The provider role that grants this app. A first sign-in with a verified email and no account creates a
    /// Member account only when the person holds it; existing accounts sign in without it. Empty = no sign-in
    /// creates an account, so only People-page invites do.
    /// </summary>
    public string RequiredRole { get; set; } = "finance-sentry";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Authority)
        && !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret)
        && Uri.TryCreate(PublicBaseUrl, UriKind.Absolute, out _);

    /// <summary>The absolute redirect URI to register with the provider.</summary>
    public string RedirectUri => PublicBaseUrl.TrimEnd('/') + CallbackPath;

    /// <summary>
    /// Where the provider returns the browser after ending its session; register it exactly as the app's
    /// post sign-out redirect URI. The <c>info</c> value holds the login page on its message instead of
    /// forwarding straight back to the provider.
    /// </summary>
    public string PostLogoutRedirectUri => PublicBaseUrl.TrimEnd('/') + "/login?info=signed_out";

    /// <summary>Where a sign-in the app refuses (no account and no app role, or an unverified email) sends the browser.</summary>
    public string AccessDeniedUrl => PublicBaseUrl.TrimEnd('/') + AccessDeniedPath;

    /// <summary>Whether the provider's role claims grant this app (<see cref="RequiredRole"/>).</summary>
    public bool GrantsApp(IEnumerable<string> roles) =>
        !string.IsNullOrWhiteSpace(RequiredRole) && roles.Contains(RequiredRole, StringComparer.Ordinal);
}
