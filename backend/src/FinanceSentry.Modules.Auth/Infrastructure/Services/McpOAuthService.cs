using FinanceSentry.Modules.Auth.Application.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Domain.Exceptions;
using Microsoft.AspNetCore.Identity;

namespace FinanceSentry.Modules.Auth.Infrastructure.Services;

public sealed class McpOAuthService(
    IMcpAuthorizationCodeStore codeStore,
    IRefreshTokenService refreshTokenService,
    ITokenService tokenService,
    UserManager<ApplicationUser> userManager) : IMcpOAuthService
{
    private const string Scope = "mcp.full_access";
    private const string TokenType = "Bearer";

    public async Task<McpOAuthTokenResponse> ExchangeAuthorizationCodeAsync(string code, string redirectUri, CancellationToken cancellationToken = default)
    {
        var payload = await codeStore.ConsumeUnscopedAsync(code, redirectUri, cancellationToken)
            ?? throw new InvalidRefreshTokenException("Invalid MCP authorization code.");

        var user = await userManager.FindByIdAsync(payload.UserId)
            ?? throw new InvalidRefreshTokenException("Invalid MCP authorization code.");

        var (refreshToken, _) = await refreshTokenService.IssueAsync(user.Id, cancellationToken);
        var (accessToken, expiresAt) = tokenService.GenerateMcpAccessToken(user);

        return new McpOAuthTokenResponse(
            accessToken,
            refreshToken,
            TokenType,
            (int)Math.Max(1, Math.Round((expiresAt - DateTime.UtcNow).TotalSeconds)),
            expiresAt,
            user.Id,
            user.Email ?? payload.Email,
            Scope);
    }

    public async Task<McpOAuthTokenResponse> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var existing = await refreshTokenService.ValidateUnscopedAsync(refreshToken, cancellationToken)
            ?? throw new InvalidRefreshTokenException();

        var user = await userManager.FindByIdAsync(existing.UserId)
            ?? throw new InvalidRefreshTokenException();

        var (nextRefreshToken, _) = await refreshTokenService.RotateAsync(existing, cancellationToken);
        var (accessToken, expiresAt) = tokenService.GenerateMcpAccessToken(user);

        return new McpOAuthTokenResponse(
            accessToken,
            nextRefreshToken,
            TokenType,
            (int)Math.Max(1, Math.Round((expiresAt - DateTime.UtcNow).TotalSeconds)),
            expiresAt,
            user.Id,
            user.Email!,
            Scope);
    }

    public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken = default)
        => refreshTokenService.RevokeTokenUnscopedAsync(refreshToken, cancellationToken);
}
