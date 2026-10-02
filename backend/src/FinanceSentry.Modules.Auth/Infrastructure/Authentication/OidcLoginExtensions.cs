namespace FinanceSentry.Modules.Auth.Infrastructure.Authentication;

using FinanceSentry.Modules.Auth.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

/// <summary>
/// Registers the org identity provider as an OpenID Connect scheme that signs into Identity's external cookie
/// (authorization-code flow with PKCE, confidential client). Registers nothing unless <c>Auth:Oidc</c> is fully
/// configured, so an unconfigured deployment behaves exactly as before.
/// </summary>
public static class OidcLoginExtensions
{
    public const string FailureRedirectCode = "OIDC_FAILED";
    public const string AccountUnavailableCode = "ACCOUNT_UNAVAILABLE";

    public static IServiceCollection AddOidcLogin(this IServiceCollection services, IConfiguration config)
    {
        var options = config.GetSection(OidcLoginOptions.SectionName).Get<OidcLoginOptions>() ?? new OidcLoginOptions();
        services.Configure<OidcLoginOptions>(config.GetSection(OidcLoginOptions.SectionName));

        if (!options.IsConfigured)
            return services;

        var secureCookies = options.PublicBaseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        services.AddAuthentication().AddOpenIdConnect(OidcLoginOptions.Scheme, o =>
        {
            o.SignInScheme = IdentityConstants.ExternalScheme;
            o.Authority = options.Authority;
            if (!string.IsNullOrWhiteSpace(options.BackchannelAuthority))
                o.BackchannelHttpHandler = new OidcAuthorityRewriteHandler(options.Authority, options.BackchannelAuthority, new HttpClientHandler());
            o.ClientId = options.ClientId;
            o.ClientSecret = options.ClientSecret;
            o.RequireHttpsMetadata = options.RequireHttpsMetadata && !BackchannelIsPlainHttp(options);
            o.ResponseType = OpenIdConnectResponseType.Code;
            o.ResponseMode = OpenIdConnectResponseMode.Query;
            o.UsePkce = true;
            o.CallbackPath = OidcLoginOptions.CallbackPath;
            o.Scope.Clear();
            o.Scope.Add("openid");
            o.Scope.Add("email");
            o.Scope.Add("profile");
            o.SaveTokens = false;
            o.MapInboundClaims = false;
            // Logto serves the email claims on userinfo, not in the ID token.
            o.GetClaimsFromUserInfoEndpoint = true;
            o.ClaimActions.MapUniqueJsonKey("email", "email");
            o.ClaimActions.MapUniqueJsonKey("email_verified", "email_verified");

            // The code flow returns by GET navigation, so Lax cookies carry the correlation and nonce back.
            o.CorrelationCookie.SameSite = SameSiteMode.Lax;
            o.NonceCookie.SameSite = SameSiteMode.Lax;
            var policy = secureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
            o.CorrelationCookie.SecurePolicy = policy;
            o.NonceCookie.SecurePolicy = policy;

            o.Events.OnRedirectToIdentityProvider = context =>
            {
                context.ProtocolMessage.RedirectUri = options.RedirectUri;
                // The browser follows this redirect, so it must target the public address even when discovery ran on the back channel.
                if (!string.IsNullOrWhiteSpace(options.BackchannelAuthority) && context.ProtocolMessage.IssuerAddress is { Length: > 0 } issuerAddress)
                    context.ProtocolMessage.IssuerAddress = OidcAuthorityRewriteHandler.ToPublic(issuerAddress, options.Authority, options.BackchannelAuthority);
                return Task.CompletedTask;
            };
            o.Events.OnAuthorizationCodeReceived = context =>
            {
                context.TokenEndpointRequest!.RedirectUri = options.RedirectUri;
                return Task.CompletedTask;
            };
            o.Events.OnRemoteFailure = context =>
            {
                context.Response.Redirect(LoginFailureUrl(options.PublicBaseUrl, FailureRedirectCode));
                context.HandleResponse();
                return Task.CompletedTask;
            };
        });

        return services;
    }

    private static bool BackchannelIsPlainHttp(OidcLoginOptions options) =>
        !string.IsNullOrWhiteSpace(options.BackchannelAuthority)
        && !options.BackchannelAuthority.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <summary>The SPA login page, carrying an error code the page turns into a message.</summary>
    public static string LoginFailureUrl(string publicBaseUrl, string code) =>
        $"{publicBaseUrl.TrimEnd('/')}/login?error={Uri.EscapeDataString(code)}";
}
