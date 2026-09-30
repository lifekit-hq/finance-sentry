using System.IdentityModel.Tokens.Jwt;
using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Auth.API.Authentication;
using FinanceSentry.Modules.Auth.Application.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceSentry.Mcp.Authentication;

/// <summary>
/// The MCP HTTP host's authentication: the shared JwtBearer registration
/// (<see cref="AccessTokenAuthenticationExtensions"/>) for <see cref="AuthAudiences.Mcp"/> tokens sent as
/// <c>Authorization: Bearer</c>, plus the default-deny fallback policy. Long-lived service tokens
/// (<c>scope=mcp.service</c>) must also still be active in <see cref="IMcpServiceTokenStore"/>, and their
/// account must still pass <see cref="AuthPolicies.RequireMcpService"/>, so revoking the token or the
/// permission takes effect on its next request; short-lived OAuth access tokens skip both checks.
/// </summary>
public static class McpAuthenticationExtensions
{
    private const string ScopeClaim = "scope";
    private const string ServiceTokenScope = "mcp.service";

    /// <summary>Registers authentication and the default-deny fallback policy. Call after
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
                var isServiceToken = IsServiceToken(context);
                if (isServiceToken && !await IsActiveServiceTokenAsync(context))
                {
                    context.Fail("The service token is revoked, expired or unknown.");
                    return;
                }

                await AccessTokenAuthenticationExtensions.LoadAccountPrincipalAsync(context);

                if (isServiceToken && context.Result is null && !await MayUseServiceTokensAsync(context))
                    context.Fail("The service token's account no longer holds the mcp.service permission.");
            };
            options.Events.OnChallenge = context =>
            {
                context.Response.Headers.WWWAuthenticate = JwtBearerDefaults.AuthenticationScheme;
                return AccessTokenAuthenticationExtensions.WriteUnauthorizedBodyAsync(context);
            };
        });

    private static bool IsServiceToken(TokenValidatedContext context)
    {
        var scopes = context.Principal?.FindFirst(ScopeClaim)?.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return scopes is not null && scopes.Contains(ServiceTokenScope);
    }

    private static async Task<bool> IsActiveServiceTokenAsync(TokenValidatedContext context) =>
        Guid.TryParse(context.Principal?.FindFirst(JwtRegisteredClaimNames.Jti)?.Value, out var jti)
        && await context.HttpContext.RequestServices.GetRequiredService<IMcpServiceTokenStore>()
            .IsActiveAsync(jti, context.HttpContext.RequestAborted);

    /// <summary>Evaluates the service-token policy against the account principal just loaded.</summary>
    private static async Task<bool> MayUseServiceTokensAsync(TokenValidatedContext context) =>
        context.Principal is not null
        && (await context.HttpContext.RequestServices.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(context.Principal, AuthPolicies.RequireMcpService)).Succeeded;
}
