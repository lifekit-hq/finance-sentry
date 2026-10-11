namespace FinanceSentry.Modules.Auth.API.Authentication;

using System.Text;
using Microsoft.IdentityModel.Tokens;

/// <summary>
/// The one <see cref="TokenValidationParameters"/> setup for access tokens, shared by every host that validates
/// them (the JwtBearer handler of the API and MCP HTTP hosts, the stdio MCP host's stored-credential check):
/// signed with <see cref="JwtOptions.Secret"/>, HS256 only, one audience, lifetime enforced with 30 s of clock
/// skew, issuer not validated (tokens carry none).
/// </summary>
public static class AccessTokenValidationParameters
{
    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    public static TokenValidationParameters Create(string secret, string audience)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);

        return new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateIssuer = false,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateLifetime = true,
            ClockSkew = ClockSkew,
        };
    }
}
