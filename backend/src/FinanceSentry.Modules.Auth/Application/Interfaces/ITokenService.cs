using FinanceSentry.Modules.Auth.Domain.Entities;

namespace FinanceSentry.Modules.Auth.Application.Interfaces;

public interface ITokenService
{
    /// <summary>
    /// Issues the app access token. <paramref name="roles"/> (the user's Identity roles) become <c>role</c>
    /// claims, so policies evaluate them from the principal; a role change applies from the next token.
    /// </summary>
    (string Token, DateTime ExpiresAt) GenerateToken(ApplicationUser user, IEnumerable<string> roles);
    (string Token, DateTime ExpiresAt) GenerateMcpAccessToken(ApplicationUser user);

    /// <summary>
    /// Issues a long-lived, revocable MCP service token (aud=mcp, scope=mcp.service) for
    /// headless first-party clients. The returned jti is persisted so it can be revoked.
    /// </summary>
    (string Token, Guid Jti, DateTime ExpiresAt) GenerateMcpServiceToken(ApplicationUser user, int lifetimeDays);
}
