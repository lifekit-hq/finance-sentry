using System.IdentityModel.Tokens.Jwt;
using System.Text;
using FinanceSentry.Core.Api;
using FinanceSentry.Modules.Auth.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace FinanceSentry.Mcp.Middleware;

public sealed class McpJwtAuthenticationMiddleware
{
    /// <summary>
    /// Paths the platform probes without a token (guardrail 1, spec 048): liveness, readiness and
    /// the Prometheus exposition. Exact, case-sensitive matches — <c>/health/x</c> still needs a token.
    /// Nothing user-scoped is served on them.
    /// </summary>
    public static readonly IReadOnlySet<string> AnonymousPaths = new HashSet<string>(StringComparer.Ordinal)
    {
        "/health",
        "/ready",
        "/metrics",
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<McpJwtAuthenticationMiddleware> _logger;
    private readonly TokenValidationParameters _validationParams;

    public McpJwtAuthenticationMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        ILogger<McpJwtAuthenticationMiddleware> logger)
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
            ValidateAudience = true,
            ValidAudience = "mcp",
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (AnonymousPaths.Contains(context.Request.Path.Value ?? string.Empty))
        {
            await _next(context);
            return;
        }

        var token = ReadToken(context);
        if (string.IsNullOrWhiteSpace(token))
        {
            await WriteUnauthorizedAsync(context, "Authentication required.", "UNAUTHORIZED");
            return;
        }

        try
        {
            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(token, _validationParams, out _);
            context.User = principal;

            // Long-lived service tokens (scope=mcp.service) are revocable — verify the jti
            // is still active. Short-lived OAuth access tokens skip this DB check.
            var scopes = principal.FindFirst("scope")?.Value?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (scopes is not null && scopes.Contains("mcp.service"))
            {
                var jtiRaw = principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
                var serviceTokenStore = context.RequestServices.GetService<IMcpServiceTokenStore>();
                if (serviceTokenStore is null
                    || !Guid.TryParse(jtiRaw, out var jti)
                    || !await serviceTokenStore.IsActiveAsync(jti, context.RequestAborted))
                {
                    _logger.LogWarning("Revoked or unrecognized MCP service token presented.");
                    await WriteUnauthorizedAsync(context, "MCP service token is no longer valid.", "TOKEN_REVOKED");
                    return;
                }
            }

            await _next(context);
        }
        catch (SecurityTokenExpiredException)
        {
            _logger.LogWarning("Expired MCP HTTP JWT token received.");
            await WriteUnauthorizedAsync(context, "Token has expired. Refresh the session and try again.", "TOKEN_EXPIRED");
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning("Invalid MCP HTTP JWT token: {Message}", ex.Message);
            await WriteUnauthorizedAsync(context, "Invalid authentication token.", "TOKEN_INVALID");
        }
    }

    private static string? ReadToken(HttpContext context)
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return authorization[prefix.Length..].Trim();
        return null;
    }

    private static async Task WriteUnauthorizedAsync(HttpContext context, string message, string code)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = "Bearer";
        await context.Response.WriteAsJsonAsync(new ApiErrorBody(message, code));
    }
}
