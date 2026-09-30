namespace FinanceSentry.Modules.BankSync.API.Middleware;

using System.IdentityModel.Tokens.Jwt;
using System.Text;
using FinanceSentry.Core.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

/// <summary>
/// Middleware that validates Bearer JWT tokens on every request.
/// Exempt paths: /health, /swagger, /api/v1/auth, the TrueLayer callback.
/// Attaches ClaimsPrincipal (including user ID) to HttpContext.User on success.
/// Returns 401 for missing/invalid/expired tokens on protected paths.
/// Optional-identity paths (/hangfire) get HttpContext.User from a valid token but are never
/// rejected here — the Hangfire dashboard's own authorization filter decides access.
/// </summary>
public class JwtAuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<JwtAuthenticationMiddleware> _logger;
    private readonly TokenValidationParameters _validationParams;

    private static readonly string[] _exemptPrefixes =
    [
        "/health",
        "/api/v1/health",
        "/metrics",
        "/swagger",
        "/api/v1/auth",
        "/api/v1/accounts/truelayer/callback"
    ];

    private static readonly string[] _optionalIdentityPrefixes =
    [
        "/hangfire"
    ];

    public JwtAuthenticationMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        ILogger<JwtAuthenticationMiddleware> logger)
    {
        _next = next;
        _logger = logger;

        var secret = configuration["Jwt:Secret"]
            ?? throw new InvalidOperationException("Jwt:Secret is not configured.");

        _validationParams = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            ValidateIssuer = false,
            ValidateAudience = false,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        if (MatchesAny(_exemptPrefixes, path))
        {
            await _next(context);
            return;
        }

        if (MatchesAny(_optionalIdentityPrefixes, path))
        {
            AttachUserIfTokenValid(context);
            await _next(context);
            return;
        }

        var token = context.Request.Cookies["fs_access_token"];
        if (string.IsNullOrWhiteSpace(token))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new ApiErrorBody("Authentication required.", "UNAUTHORIZED"));
            return;
        }

        try
        {
            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(token, _validationParams, out _);
            context.User = principal;
            await _next(context);
        }
        catch (SecurityTokenExpiredException)
        {
            _logger.LogWarning("Expired JWT token received.");
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new ApiErrorBody("Token has expired. Please sign in again.", "TOKEN_EXPIRED"));
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning("Invalid JWT token: {Message}", ex.Message);
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new ApiErrorBody("Invalid authentication token.", "TOKEN_INVALID"));
        }
    }

    private void AttachUserIfTokenValid(HttpContext context)
    {
        var token = context.Request.Cookies["fs_access_token"];
        if (string.IsNullOrWhiteSpace(token))
            return;

        try
        {
            context.User = new JwtSecurityTokenHandler().ValidateToken(token, _validationParams, out _);
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
        {
            _logger.LogWarning("Invalid JWT token on an optional-identity path: {Message}", ex.Message);
        }
    }

    private static bool MatchesAny(string[] prefixes, string path)
    {
        foreach (var prefix in prefixes)
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}
