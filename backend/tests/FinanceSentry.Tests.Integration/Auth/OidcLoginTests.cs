namespace FinanceSentry.Tests.Integration.Auth;

using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Xunit;

/// <summary>An API host with the org OIDC login configured; the provider's discovery document is stubbed in.</summary>
public class OidcApiFactory : AuthApiFactory
{
    public const string AuthorizationEndpoint = "https://idp.test/oidc/auth";
    public const string PublicBaseUrl = "https://app.test";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Auth:Oidc:Authority", "https://idp.test/oidc");
        builder.UseSetting("Auth:Oidc:ClientId", "finance-sentry");
        builder.UseSetting("Auth:Oidc:ClientSecret", "test-secret");
        builder.UseSetting("Auth:Oidc:PublicBaseUrl", PublicBaseUrl);
        builder.ConfigureServices(services => services.PostConfigure<OpenIdConnectOptions>("lifekit", o =>
        {
            var discovery = new OpenIdConnectConfiguration
            {
                Issuer = "https://idp.test/oidc",
                AuthorizationEndpoint = AuthorizationEndpoint,
                TokenEndpoint = "https://idp.test/oidc/token",
            };
            o.Configuration = discovery;
            o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(discovery);
        }));
    }

    /// <summary>The external-cookie value the OIDC handler would leave after a successful provider round trip.</summary>
    public string ExternalCookie(string subject, string? email, string emailVerified = "true", string? returnUrl = null)
    {
        var claims = new List<Claim> { new("sub", subject), new("email_verified", emailVerified) };
        if (email is not null)
            claims.Add(new Claim("email", email));

        var properties = new AuthenticationProperties();
        if (returnUrl is not null)
            properties.Items["returnUrl"] = returnUrl;

        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(new ClaimsIdentity(claims, "lifekit")), properties, IdentityConstants.ExternalScheme);
        var cookieOptions = Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ExternalScheme);
        return cookieOptions.TicketDataFormat.Protect(ticket);
    }

    public HttpClient ExternalClient(string cookie)
    {
        using var scope = Services.CreateScope();
        var name = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ExternalScheme).Cookie.Name!;
        return CookieClient((name, cookie));
    }
}

/// <summary>
/// GET /api/v1/auth/oidc/start and /oidc/callback: the org identity provider proves who the person is, the
/// account must already exist (an invite) and be active, and the session is the same as any other sign-in.
/// </summary>
public class OidcLoginTests(OidcApiFactory factory) : IClassFixture<OidcApiFactory>
{
    private const string LoginUrlPrefix = OidcApiFactory.PublicBaseUrl + "/login?error=";

    [Fact]
    public async Task Methods_ReportsOidcEnabled()
    {
        var methods = await factory.CookieClient().GetFromJsonAsync<MethodsShape>("/api/v1/auth/methods");

        methods!.Oidc.Should().BeTrue();
    }

    [Fact]
    public async Task Start_RedirectsToTheProviderWithPkceAndTheConfiguredRedirectUri()
    {
        var response = await factory.CookieClient().GetAsync("/api/v1/auth/oidc/start");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location!.ToString();
        location.Should().StartWith(OidcApiFactory.AuthorizationEndpoint);
        location.Should().Contain("response_type=code").And.Contain("client_id=finance-sentry")
            .And.Contain("code_challenge_method=S256").And.Contain("code_challenge=")
            .And.Contain("scope=openid email profile")
            .And.Contain(Uri.EscapeDataString(OidcApiFactory.PublicBaseUrl + "/api/v1/auth/oidc/signin"));
    }

    [Fact]
    public async Task Callback_WithoutAProviderSession_GoesBackToLoginWithAnError()
    {
        var response = await factory.CookieClient().GetAsync("/api/v1/auth/oidc/callback");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be(LoginUrlPrefix + "OIDC_FAILED");
    }

    [Fact]
    public async Task Callback_InvitedMember_SignsInWithTheSameCookiesAndLinksTheProvider()
    {
        await factory.CreatePendingInviteAsync("oidc-invited@test.com");

        var cookie = factory.ExternalCookie("lk-sub-invited", "oidc-invited@test.com", returnUrl: "/budgets");
        var response = await factory.ExternalClient(cookie).GetAsync("/api/v1/auth/oidc/callback");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be(OidcApiFactory.PublicBaseUrl + "/budgets");
        var setCookies = response.Headers.GetValues("Set-Cookie").ToList();
        setCookies.Should().Contain(c => c.Contains("refresh", StringComparison.OrdinalIgnoreCase) && c.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        setCookies.Should().Contain(c => c.StartsWith("fs_access_token=", StringComparison.Ordinal));

        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        (await users.FindByLoginAsync("lifekit", "lk-sub-invited")).Should().NotBeNull();
    }

    [Fact]
    public async Task Callback_ExistingAccountWithPassword_RelinksByVerifiedEmail()
    {
        await factory.EnsureUserExistsAsync("oidc-relink@test.com", "TestPass123!");

        var cookie = factory.ExternalCookie("lk-sub-relink", "oidc-relink@test.com");
        var response = await factory.ExternalClient(cookie).GetAsync("/api/v1/auth/oidc/callback");

        response.Headers.Location!.ToString().Should().Be(OidcApiFactory.PublicBaseUrl + "/");
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var linked = await users.FindByLoginAsync("lifekit", "lk-sub-relink");
        linked!.Email.Should().Be("oidc-relink@test.com");
    }

    [Fact]
    public async Task Callback_UninvitedEmail_IsRefusedAndCreatesNothing()
    {
        var cookie = factory.ExternalCookie("lk-sub-stranger", "stranger@test.com");
        var response = await factory.ExternalClient(cookie).GetAsync("/api/v1/auth/oidc/callback");

        response.Headers.Location!.ToString().Should().Be(LoginUrlPrefix + "ACCOUNT_NOT_INVITED");
        NoSessionCookies(response);
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        (await users.FindByEmailAsync("stranger@test.com")).Should().BeNull();
    }

    [Fact]
    public async Task Callback_UnverifiedEmail_IsNotMatchedToAnAccount()
    {
        await factory.EnsureUserExistsAsync("oidc-unverified@test.com", "TestPass123!");

        var cookie = factory.ExternalCookie("lk-sub-unverified", "oidc-unverified@test.com", emailVerified: "false");
        var response = await factory.ExternalClient(cookie).GetAsync("/api/v1/auth/oidc/callback");

        response.Headers.Location!.ToString().Should().Be(LoginUrlPrefix + "ACCOUNT_NOT_INVITED");
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        (await users.FindByLoginAsync("lifekit", "lk-sub-unverified")).Should().BeNull();
    }

    [Fact]
    public async Task Callback_LockedAccount_IsRefusedAndLinksNothing()
    {
        await factory.EnsureUserExistsAsync("oidc-locked@test.com", "TestPass123!");
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByEmailAsync("oidc-locked@test.com");
            await users.SetLockoutEndDateAsync(user!, DateTimeOffset.UtcNow.AddHours(1));
        }

        var cookie = factory.ExternalCookie("lk-sub-locked", "oidc-locked@test.com");
        var response = await factory.ExternalClient(cookie).GetAsync("/api/v1/auth/oidc/callback");

        response.Headers.Location!.ToString().Should().Be(LoginUrlPrefix + "ACCOUNT_UNAVAILABLE");
        NoSessionCookies(response);
        using var check = factory.Services.CreateScope();
        (await check.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
            .FindByLoginAsync("lifekit", "lk-sub-locked")).Should().BeNull();
    }

    [Theory]
    [InlineData("//evil.test")]
    [InlineData("/\\evil.test")]
    [InlineData("https://evil.test/")]
    public async Task Callback_NonLocalReturnUrl_FallsBackToTheAppRoot(string returnUrl)
    {
        await factory.CreatePendingInviteAsync("oidc-redirect@test.com");

        var cookie = factory.ExternalCookie("lk-sub-redirect", "oidc-redirect@test.com", returnUrl: returnUrl);
        var response = await factory.ExternalClient(cookie).GetAsync("/api/v1/auth/oidc/callback");

        response.Headers.Location!.ToString().Should().Be(OidcApiFactory.PublicBaseUrl + "/");
    }

    // Only the provider-session cookie is cleared; no refresh or access token is issued.
    private static void NoSessionCookies(HttpResponseMessage response) =>
        (response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [])
            .Should().NotContain(c => c.StartsWith("fs_access_token=", StringComparison.Ordinal)
                                      || c.Contains("refresh", StringComparison.OrdinalIgnoreCase));

    private sealed record MethodsShape(bool Oidc, bool PasswordLogin, bool GoogleDirect);
}

/// <summary>Merging the feature changes nothing until it is configured.</summary>
public class OidcDisabledTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    [Fact]
    public async Task Methods_ReportsOidcDisabled()
    {
        var methods = await factory.CookieClient().GetFromJsonAsync<MethodsShape>("/api/v1/auth/methods");

        methods!.Oidc.Should().BeFalse();
    }

    [Fact]
    public async Task Start_WhenNotConfigured_IsRefused()
    {
        var response = await factory.CookieClient().GetAsync("/api/v1/auth/oidc/start");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Callback_WhenNotConfigured_IsRefused()
    {
        var response = await factory.CookieClient().GetAsync("/api/v1/auth/oidc/callback");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private sealed record MethodsShape(bool Oidc, bool PasswordLogin, bool GoogleDirect);
}
