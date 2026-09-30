using System.IdentityModel.Tokens.Jwt;
using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Auth.API.Authentication;
using FinanceSentry.Modules.Auth.Application.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceSentry.Mcp.Authentication;

/// <summary>
/// The MCP HTTP host's authentication: the shared JwtBearer registration
/// (<see cref="AccessTokenAuthenticationExtensions"/>) for <see cref="AuthAudiences.Mcp"/> tokens sent as
/// <c>Authorization: Bearer</c>, plus the fallback policy that requires an authenticated user. Long-lived
/// service tokens (<c>scope=mcp.service</c>) must also still be active in <see cref="IMcpServiceTokenStore"/>,
/// so revoking one takes effect on its next request; short-lived OAuth access tokens skip that lookup.
/// </summary>
public static class McpAuthenticationExtensions
{
    private const string ScopeClaim = "scope";
    private const string ServiceTokenScope = "mcp.service";

    /// <summary>Registers authentication and the fallback policy. Call after
    /// <see cref="McpServiceRegistration.RegisterShared"/> (Identity registers its own cookie scheme as the
    /// default; this replaces that default).</summary>
    public static IServiceCollection AddMcpAuthentication(this IServiceCollection services) =>
        services.AddAccessTokenAuthentication(AuthAudiences.Mcp, options =>
        {
            // Keep the token's claim names (scope, jti) as issued; the request principal is rebuilt from the
            // local account either way.
            options.MapInboundClaims = false;
            options.Events.OnTokenValidated = async context =>
            {
                if (!await IsUsableServiceTokenAsync(context))
                {
                    context.Fail("The service token is revoked, expired or unknown.");
                    return;
                }

                await AccessTokenAuthenticationExtensions.LoadAccountPrincipalAsync(context);
            };
            options.Events.OnChallenge = context =>
            {
                context.Response.Headers.WWWAuthenticate = JwtBearerDefaults.AuthenticationScheme;
                return AccessTokenAuthenticationExtensions.WriteUnauthorizedBodyAsync(context);
            };
        });

    /// <summary>True for any token that is not a service token, and for a service token whose jti is active.</summary>
    private static async Task<bool> IsUsableServiceTokenAsync(TokenValidatedContext context)
    {
        var scopes = context.Principal?.FindFirst(ScopeClaim)?.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (scopes is null || !scopes.Contains(ServiceTokenScope))
            return true;

        return Guid.TryParse(context.Principal?.FindFirst(JwtRegisteredClaimNames.Jti)?.Value, out var jti)
            && await context.HttpContext.RequestServices.GetRequiredService<IMcpServiceTokenStore>()
                .IsActiveAsync(jti, context.HttpContext.RequestAborted);
    }
}
