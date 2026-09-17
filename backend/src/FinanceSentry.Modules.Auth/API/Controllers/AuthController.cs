using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Auth.Application.Commands;
using FinanceSentry.Modules.Auth.Application.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Exceptions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace FinanceSentry.Modules.Auth.API.Controllers;

[ApiController]
[Route("auth")]
public class AuthController(
    ICommandHandler<LoginCommand, AuthResult> loginHandler,
    ICommandHandler<RegisterCommand, AuthResult> registerHandler,
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
    private const string RefreshTokenCookie = "fs_refresh_token";
    private const string AccessTokenCookie = "fs_access_token";

    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var rawToken = Request.Cookies[RefreshTokenCookie];
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

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] AuthRequest request)
    {
        var result = await loginHandler.Handle(new LoginCommand(request.Email, request.Password), HttpContext.RequestAborted);
        SetRefreshTokenCookie(result.RawRefreshToken);
        SetAccessTokenCookie(result.RawAccessToken, result.Response.ExpiresAt);
        return Ok(result.Response);
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] AuthRequest request)
    {
        var result = await registerHandler.Handle(new RegisterCommand(request.Email, request.Password), HttpContext.RequestAborted);
        SetRefreshTokenCookie(result.RawRefreshToken);
        SetAccessTokenCookie(result.RawAccessToken, result.Response.ExpiresAt);
        return Created(string.Empty, result.Response);
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh()
    {
        var rawToken = Request.Cookies[RefreshTokenCookie];
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

    [HttpPost("google/verify")]
    public async Task<IActionResult> GoogleVerify([FromBody] VerifyGoogleCredentialRequest request)
    {
        var result = await googleVerifyHandler.Handle(new VerifyGoogleCredentialCommand(request.Credential), HttpContext.RequestAborted);
        SetRefreshTokenCookie(result.RawRefreshToken);
        SetAccessTokenCookie(result.RawAccessToken, result.Response.ExpiresAt);
        return Ok(result.Response);
    }

    [HttpGet("mcp/authorize")]
    public async Task<IActionResult> AuthorizeMcp([FromQuery] string redirectUri, [FromQuery] string? state = null)
    {
        var rawToken = Request.Cookies[RefreshTokenCookie];
        if (string.IsNullOrWhiteSpace(rawToken))
            throw new InvalidRefreshTokenException("No session found.");

        var result = await authorizeMcpHandler.Handle(
            new AuthorizeMcpCommand(rawToken, redirectUri, state),
            HttpContext.RequestAborted);
        return Redirect(result.RedirectUrl);
    }

    [HttpPost("mcp/token")]
    public async Task<IActionResult> ExchangeMcpToken([FromBody] McpTokenRequest request)
    {
        var response = await exchangeMcpTokenHandler.Handle(
            new ExchangeMcpTokenCommand(request.GrantType, request.Code, request.RedirectUri, request.RefreshToken),
            HttpContext.RequestAborted);
        return Ok(response);
    }

    [HttpPost("mcp/revoke")]
    public async Task<IActionResult> RevokeMcpToken([FromBody] McpRevokeRequest request)
    {
        await revokeMcpTokenHandler.Handle(new RevokeMcpTokenCommand(request.RefreshToken), HttpContext.RequestAborted);
        return NoContent();
    }

    // Mints a long-lived, revocable service token for headless first-party MCP clients
    // (e.g. the OpenClaw gateway) that cannot perform the interactive OAuth refresh flow.
    [HttpPost("mcp/service-token")]
    public async Task<IActionResult> IssueMcpServiceToken([FromBody] McpServiceTokenRequest request)
    {
        var rawToken = Request.Cookies[RefreshTokenCookie];
        if (string.IsNullOrWhiteSpace(rawToken))
            throw new InvalidRefreshTokenException("No session found.");

        var result = await issueMcpServiceTokenHandler.Handle(
            new IssueMcpServiceTokenCommand(rawToken, request.Label, request.LifetimeDays),
            HttpContext.RequestAborted);
        return Ok(result);
    }

    [HttpPost("mcp/service-token/revoke")]
    public async Task<IActionResult> RevokeMcpServiceToken([FromBody] McpServiceTokenRevokeRequest request)
    {
        var rawToken = Request.Cookies[RefreshTokenCookie];
        if (string.IsNullOrWhiteSpace(rawToken))
            throw new InvalidRefreshTokenException("No session found.");

        await revokeMcpServiceTokenHandler.Handle(
            new RevokeMcpServiceTokenCommand(rawToken, request.Jti),
            HttpContext.RequestAborted);
        return NoContent();
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var rawToken = Request.Cookies[RefreshTokenCookie];

        if (!string.IsNullOrWhiteSpace(rawToken))
            await logoutHandler.Handle(new LogoutCommand(rawToken), HttpContext.RequestAborted);

        DeleteRefreshTokenCookie();
        DeleteAccessTokenCookie();
        return NoContent();
    }

    private void SetRefreshTokenCookie(string rawToken)
    {
        Response.Cookies.Append(RefreshTokenCookie, rawToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = env.IsProduction(),
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddDays(30),
            Path = "/"
        });
    }

    private void SetAccessTokenCookie(string rawToken, DateTime expiresAt)
    {
        Response.Cookies.Append(AccessTokenCookie, rawToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = env.IsProduction(),
            SameSite = SameSiteMode.Strict,
            Expires = new DateTimeOffset(expiresAt, TimeSpan.Zero),
            Path = "/"
        });
    }

    private void DeleteRefreshTokenCookie()
    {
        Response.Cookies.Delete(RefreshTokenCookie, new CookieOptions
        {
            HttpOnly = true,
            Secure = env.IsProduction(),
            SameSite = SameSiteMode.Strict,
            Path = "/"
        });
    }

    private void DeleteAccessTokenCookie()
    {
        Response.Cookies.Delete(AccessTokenCookie, new CookieOptions
        {
            HttpOnly = true,
            Secure = env.IsProduction(),
            SameSite = SameSiteMode.Strict,
            Path = "/"
        });
    }
}
