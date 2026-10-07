using FinanceSentry.Core.Api;
using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Auth.API.Authentication;
using FinanceSentry.Modules.Auth.Application.Commands;
using FinanceSentry.Modules.Auth.Application.Interfaces;
using FinanceSentry.Core.Exceptions;
using FinanceSentry.Modules.Auth.Domain.Exceptions;
using FinanceSentry.Modules.Auth.Infrastructure.Authentication;
using FinanceSentry.Modules.Auth.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using System.Security.Claims;

namespace FinanceSentry.Modules.Auth.API.Controllers;

[ApiController]
[EnableRateLimiting(RateLimitingPolicies.Anonymous)]
[Route("auth")]
public class AuthController(
    ICommandHandler<LoginCommand, AuthResult> loginHandler,
    ICommandHandler<AcceptInviteCommand, AuthResult> acceptInviteHandler,
    ICommandHandler<RefreshCommand, AuthResult> refreshHandler,
    ICommandHandler<ExternalLoginCommand, AuthResult> externalLoginHandler,
    ICommandHandler<LogoutCommand, Unit> logoutHandler,
    ICommandHandler<AuthorizeMcpCommand, AuthorizeMcpResult> authorizeMcpHandler,
    ICommandHandler<ExchangeMcpTokenCommand, McpOAuthTokenResponse> exchangeMcpTokenHandler,
    ICommandHandler<RevokeMcpTokenCommand, Unit> revokeMcpTokenHandler,
    ICommandHandler<IssueMcpServiceTokenCommand, IssueMcpServiceTokenResult> issueMcpServiceTokenHandler,
    ICommandHandler<RevokeMcpServiceTokenCommand, Unit> revokeMcpServiceTokenHandler,
    IQueryHandler<GetMeQuery, GetMeResult> getMeHandler,
    IWebHostEnvironment env,
    IOptions<AuthSignInOptions> signInOptions,
    IOptions<OidcLoginOptions> oidcOptions) : ControllerBase
{
    private const int RefreshTokenCookieDays = 30;
    private const string ReturnUrlItem = "returnUrl";

    private bool SecureCookies => env.IsProduction();

    [AllowAnonymous]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var rawToken = ReadRefreshTokenCookie();
        if (string.IsNullOrWhiteSpace(rawToken))
            throw new InvalidRefreshTokenException("No session found.");

        try
        {
            var result = await getMeHandler.Handle(new GetMeQuery(rawToken), HttpContext.RequestAborted);
            SetAccessTokenCookie(result.RawAccessToken, result.Response.ExpiresAt);
            return Ok(result.Response);
        }
        catch (InvalidRefreshTokenException)
        {
            DeleteRefreshTokenCookie();
            throw;
        }
    }

    /// <summary>Which sign-in methods this deployment offers, so the login page shows only those.</summary>
    [AllowAnonymous]
    [HttpGet("methods")]
    public IActionResult Methods() => Ok(new SignInMethodsResponse(
        Oidc: oidcOptions.Value.IsConfigured,
        PasswordLogin: signInOptions.Value.PasswordLogin.Enabled));

    // Sends the browser to the org identity provider. The provider returns to the OIDC handler's callback
    // (OidcLoginOptions.CallbackPath), which signs into Identity's external cookie and lands on OidcCallback.
    [AllowAnonymous]
    [HttpGet("oidc/start")]
    public IActionResult OidcStart([FromQuery] string? returnUrl = null)
    {
        if (!oidcOptions.Value.IsConfigured)
            throw new SignInMethodDisabledException();

        var properties = new AuthenticationProperties { RedirectUri = Url.Action(nameof(OidcCallback), "Auth") };
        if (IsLocalPath(returnUrl))
            properties.Items[ReturnUrlItem] = returnUrl;

        return Challenge(properties, OidcLoginOptions.Scheme);
    }

    // The provider has proven who the person is; the account itself still has to exist (an invite) and be active.
    // Failures go back to the SPA login page as an error code rather than a JSON body, since this is a browser navigation.
    [AllowAnonymous]
    [HttpGet("oidc/callback")]
    public async Task<IActionResult> OidcCallback()
    {
        var options = oidcOptions.Value;
        if (!options.IsConfigured)
            throw new SignInMethodDisabledException();

        var external = await HttpContext.AuthenticateAsync(IdentityConstants.ExternalScheme);
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

        var subject = external.Principal?.FindFirstValue("sub");
        var email = external.Principal?.FindFirstValue("email");
        if (!external.Succeeded || string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(email))
            return Redirect(OidcLoginExtensions.LoginFailureUrl(options.PublicBaseUrl, OidcLoginExtensions.FailureRedirectCode));

        var emailVerified = bool.TryParse(external.Principal!.FindFirstValue("email_verified"), out var verified) && verified;

        try
        {
            var result = await externalLoginHandler.Handle(
                new ExternalLoginCommand(OidcLoginOptions.Scheme, subject, email, emailVerified),
                HttpContext.RequestAborted);
            SetRefreshTokenCookie(result.RawRefreshToken);
            SetAccessTokenCookie(result.RawAccessToken, result.Response.ExpiresAt);
            if (external.Properties?.GetTokenValue(OpenIdConnectParameterNames.IdToken) is { Length: > 0 } idToken)
                AuthCookies.Write(Response, AuthCookies.OidcIdToken, idToken,
                    DateTimeOffset.UtcNow.AddDays(RefreshTokenCookieDays), SecureCookies);
        }
        catch (ApiException ex)
        {
            // A locked or revoked account is the generic invalid-credentials error; name it for the login page.
            var code = ex is InvalidCredentialsException ? OidcLoginExtensions.AccountUnavailableCode : ex.ErrorCode;
            return Redirect(OidcLoginExtensions.LoginFailureUrl(options.PublicBaseUrl, code));
        }

        string? returnUrl = null;
        external.Properties?.Items.TryGetValue(ReturnUrlItem, out returnUrl);
        return Redirect(options.PublicBaseUrl.TrimEnd('/') + (IsLocalPath(returnUrl) ? returnUrl : "/"));
    }

    // Only an absolute path on this app is a valid return target: a scheme-relative ("//host") or backslash form
    // would be an open redirect.
    private static bool IsLocalPath(string? path) =>
        !string.IsNullOrEmpty(path) && path[0] == '/' && !path.StartsWith("//") && !path.StartsWith("/\\");

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] AuthRequest request)
    {
        var result = await loginHandler.Handle(new LoginCommand(request.Email, request.Password), HttpContext.RequestAborted);
        SetRefreshTokenCookie(result.RawRefreshToken);
        SetAccessTokenCookie(result.RawAccessToken, result.Response.ExpiresAt);
        return Ok(result.Response);
    }

    // Sets an invited account's first password from the one-time invite token and signs it in.
    [AllowAnonymous]
    [HttpPost("invite/accept")]
    public async Task<IActionResult> AcceptInvite([FromBody] AcceptInviteRequest request)
    {
        var result = await acceptInviteHandler.Handle(
            new AcceptInviteCommand(request.UserId, request.Token, request.Password),
            HttpContext.RequestAborted);
        SetRefreshTokenCookie(result.RawRefreshToken);
        SetAccessTokenCookie(result.RawAccessToken, result.Response.ExpiresAt);
        return Ok(result.Response);
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh()
    {
        var rawToken = ReadRefreshTokenCookie();
        if (string.IsNullOrWhiteSpace(rawToken))
            throw new InvalidRefreshTokenException("Refresh token missing.");

        try
        {
            var result = await refreshHandler.Handle(new RefreshCommand(rawToken), HttpContext.RequestAborted);
            SetRefreshTokenCookie(result.RawRefreshToken);
            SetAccessTokenCookie(result.RawAccessToken, result.Response.ExpiresAt);
            return Ok(result.Response);
        }
        catch (InvalidRefreshTokenException)
        {
            DeleteRefreshTokenCookie();
            throw;
        }
    }

    [AllowAnonymous]
    [HttpGet("mcp/authorize")]
    public async Task<IActionResult> AuthorizeMcp([FromQuery] string redirectUri, [FromQuery] string? state = null)
    {
        var rawToken = ReadRefreshTokenCookie();
        if (string.IsNullOrWhiteSpace(rawToken))
            throw new InvalidRefreshTokenException("No session found.");

        var result = await authorizeMcpHandler.Handle(
            new AuthorizeMcpCommand(rawToken, redirectUri, state),
            HttpContext.RequestAborted);
        return Redirect(result.RedirectUrl);
    }

    [AllowAnonymous]
    [HttpPost("mcp/token")]
    public async Task<IActionResult> ExchangeMcpToken([FromBody] McpTokenRequest request)
    {
        var response = await exchangeMcpTokenHandler.Handle(
            new ExchangeMcpTokenCommand(request.GrantType, request.Code, request.RedirectUri, request.RefreshToken),
            HttpContext.RequestAborted);
        return Ok(response);
    }

    [AllowAnonymous]
    [HttpPost("mcp/revoke")]
    public async Task<IActionResult> RevokeMcpToken([FromBody] McpRevokeRequest request)
    {
        await revokeMcpTokenHandler.Handle(new RevokeMcpTokenCommand(request.RefreshToken), HttpContext.RequestAborted);
        return NoContent();
    }

    // Mints a long-lived, revocable service token for headless first-party MCP clients
    // (e.g. the OpenClaw gateway) that cannot perform the interactive OAuth refresh flow.
    [Authorize(Policy = AuthPolicies.RequireMcpService)]
    [HttpPost("mcp/service-token")]
    public async Task<IActionResult> IssueMcpServiceToken([FromBody] McpServiceTokenRequest request)
    {
        var rawToken = ReadRefreshTokenCookie();
        if (string.IsNullOrWhiteSpace(rawToken))
            throw new InvalidRefreshTokenException("No session found.");

        var result = await issueMcpServiceTokenHandler.Handle(
            new IssueMcpServiceTokenCommand(rawToken, request.Label, request.LifetimeDays),
            HttpContext.RequestAborted);
        return Ok(result);
    }

    [Authorize(Policy = AuthPolicies.RequireMcpService)]
    [HttpPost("mcp/service-token/revoke")]
    public async Task<IActionResult> RevokeMcpServiceToken([FromBody] McpServiceTokenRevokeRequest request)
    {
        var rawToken = ReadRefreshTokenCookie();
        if (string.IsNullOrWhiteSpace(rawToken))
            throw new InvalidRefreshTokenException("No session found.");

        await revokeMcpServiceTokenHandler.Handle(
            new RevokeMcpServiceTokenCommand(rawToken, request.Jti),
            HttpContext.RequestAborted);
        return NoContent();
    }

    [AllowAnonymous]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var rawToken = ReadRefreshTokenCookie();

        if (!string.IsNullOrWhiteSpace(rawToken))
            await logoutHandler.Handle(new LogoutCommand(rawToken), HttpContext.RequestAborted);

        var idToken = AuthCookies.Read(Request.Cookies, AuthCookies.OidcIdToken, SecureCookies);
        DeleteRefreshTokenCookie();
        DeleteAccessTokenCookie();
        AuthCookies.Delete(Response, AuthCookies.OidcIdToken, SecureCookies);

        // A session that came from the provider ends there too (RP-initiated logout); otherwise the next sign-in
        // would re-enter silently on the provider's still-live session.
        if (!oidcOptions.Value.IsConfigured || string.IsNullOrWhiteSpace(idToken))
            return NoContent();

        var properties = new AuthenticationProperties();
        properties.StoreTokens([new AuthenticationToken { Name = OpenIdConnectParameterNames.IdToken, Value = idToken }]);
        await HttpContext.SignOutAsync(OidcLoginOptions.Scheme, properties);
        return HttpContext.Items[OidcLoginExtensions.EndSessionUrlItem] is string endSessionUrl
            ? Ok(new LogoutResponse(endSessionUrl))
            : NoContent();
    }

    private string? ReadRefreshTokenCookie() =>
        AuthCookies.Read(Request.Cookies, AuthCookies.RefreshToken, SecureCookies);

    private void SetRefreshTokenCookie(string rawToken) =>
        AuthCookies.Write(Response, AuthCookies.RefreshToken, rawToken,
            DateTimeOffset.UtcNow.AddDays(RefreshTokenCookieDays), SecureCookies);

    private void SetAccessTokenCookie(string rawToken, DateTime expiresAt) =>
        AuthCookies.Write(Response, AuthCookies.AccessToken, rawToken,
            new DateTimeOffset(expiresAt, TimeSpan.Zero), SecureCookies);

    private void DeleteRefreshTokenCookie() =>
        AuthCookies.Delete(Response, AuthCookies.RefreshToken, SecureCookies);

    private void DeleteAccessTokenCookie() =>
        AuthCookies.Delete(Response, AuthCookies.AccessToken, SecureCookies);
}
