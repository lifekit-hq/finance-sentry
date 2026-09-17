using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FinanceSentry.Modules.Auth.Application.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace FinanceSentry.Modules.Auth.Infrastructure.Services;

public class JwtTokenService(IConfiguration configuration) : ITokenService
{
    public const string McpAudience = "mcp";
    private const string McpScope = "mcp.full_access";
    private const string McpServiceScope = "mcp.service";
    private const int McpAccessTokenLifetimeMinutes = 15;

    private readonly string _secret = configuration["Jwt:Secret"] ?? throw new InvalidOperationException("Jwt:Secret is not configured.");
    private readonly int _expiryMinutes = int.TryParse(configuration["Jwt:ExpiryMinutes"], out var minutes) ? minutes : 60;

    public (string Token, DateTime ExpiresAt) GenerateToken(ApplicationUser user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var now = DateTime.UtcNow;
        var expiresAt = now.AddMinutes(_expiryMinutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id),
            new Claim(JwtRegisteredClaimNames.Email, user.Email!),
            new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        var token = new JwtSecurityToken(
            claims: claims,
            notBefore: now,
            expires: expiresAt,
            signingCredentials: credentials
        );

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    public (string Token, DateTime ExpiresAt) GenerateMcpAccessToken(ApplicationUser user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var now = DateTime.UtcNow;
        var expiresAt = now.AddMinutes(McpAccessTokenLifetimeMinutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id),
            new Claim(JwtRegisteredClaimNames.Email, user.Email!),
            new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new Claim(JwtRegisteredClaimNames.Aud, McpAudience),
            new Claim("scope", McpScope),
            new Claim(ClaimTypes.NameIdentifier, user.Id),
        };

        var token = new JwtSecurityToken(
            claims: claims,
            notBefore: now,
            expires: expiresAt,
            signingCredentials: credentials
        );

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    public (string Token, Guid Jti, DateTime ExpiresAt) GenerateMcpServiceToken(ApplicationUser user, int lifetimeDays)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var now = DateTime.UtcNow;
        var jti = Guid.NewGuid();
        var expiresAt = now.AddDays(lifetimeDays);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id),
            new Claim(JwtRegisteredClaimNames.Email, user.Email!),
            new Claim(JwtRegisteredClaimNames.Jti, jti.ToString()),
            new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new Claim(JwtRegisteredClaimNames.Aud, McpAudience),
            new Claim("scope", McpServiceScope),
            new Claim(ClaimTypes.NameIdentifier, user.Id),
        };

        var token = new JwtSecurityToken(
            claims: claims,
            notBefore: now,
            expires: expiresAt,
            signingCredentials: credentials
        );

        return (new JwtSecurityTokenHandler().WriteToken(token), jti, expiresAt);
    }
}
