using FinanceSentry.Modules.Auth.Domain.Entities;

namespace FinanceSentry.Modules.Auth.Application.Interfaces;

public interface ITokenService
{
    (string Token, DateTime ExpiresAt) GenerateToken(ApplicationUser user);
    (string Token, DateTime ExpiresAt) GenerateMcpAccessToken(ApplicationUser user);

    /// <summary>
    /// Issues a long-lived, revocable MCP service token (aud=mcp, scope=mcp.service) for
    /// headless first-party clients. The returned jti is persisted so it can be revoked.
    /// </summary>
    (string Token, Guid Jti, DateTime ExpiresAt) GenerateMcpServiceToken(ApplicationUser user, int lifetimeDays);
}
