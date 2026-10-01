using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Auth.API.Authentication;
using FinanceSentry.Modules.Auth.Application.Commands;
using FinanceSentry.Modules.Auth.Application.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace FinanceSentry.Modules.Auth.API.Controllers;

[ApiController]
[Route("auth")]
public class AuthController(
    ICommandHandler<LoginCommand, AuthResult> loginHandler,
    ICommandHandler<AcceptInviteCommand, AuthResult> acceptInviteHandler,
    ICommandHandler<RefreshCommand, AuthResult> refreshHandler,
    ICommandHandler<VerifyGoogleCredentialCommand, AuthResult> googleVerifyHandler,
    ICommandHandler<LogoutCommand, Unit> logoutHandler,
    ICommandHandler<AuthorizeMcpCommand, AuthorizeMcpResult> authorizeMcpHandler,
    ICommandHandler<ExchangeMcpTokenCommand, McpOAuthTokenResponse> exchangeMcpTokenHandler,
    ICommandHandler<RevokeMcpTokenCommand, Unit> revokeMcpTokenHandler,
    ICommandHandler<IssueMcpServiceTokenCommand, IssueMcpServiceTokenResult> issueMcpServiceTokenHandler,
    ICommandHandler<RevokeMcpServiceTokenCommand, Unit> revokeMcpServiceTokenHandler,
    IQueryHandler<GetMeQuery, GetMeResult> getMeHandler,
    IWebHostEnvironment env) : ControllerBase
{
    private const int RefreshTokenCookieDays = 30;

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
    [HttpPost("google/verify")]
    public async Task<IActionResult> GoogleVerify([FromBody] VerifyGoogleCredentialRequest request)
    {
        var result = await googleVerifyHandler.Handle(new VerifyGoogleCredentialCommand(request.Credential), HttpContext.RequestAborted);
        SetRefreshTokenCookie(result.RawRefreshToken);
        SetAccessTokenCookie(result.RawAccessToken, result.Response.ExpiresAt);
        return Ok(result.Response);
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

        DeleteRefreshTokenCookie();
        DeleteAccessTokenCookie();
        return NoContent();
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
