using FinanceSentry.Core.Auth;
using FinanceSentry.Mcp.Authentication;
using FinanceSentry.Mcp.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceSentry.Mcp;

/// <summary>
/// The Streamable HTTP transport's services and request pipeline. Extracted from <c>Program.cs</c> so tests
/// run the exact endpoints and authentication the host serves. The MCP endpoint requires the
/// <c>mcp.connect</c> permission (<see cref="AuthPolicies.RequireMcpConnect"/>); the platform probes are mapped
/// <see cref="Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute"/>; anything else hits the
/// default-deny fallback policy.
/// </summary>
public static class McpHttpHost
{
    public static void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        McpServiceRegistration.RegisterShared(services, configuration);
        services.AddMcpPlatformEndpoints(configuration);

        // After RegisterShared: Identity (Auth module) registers its cookie scheme as the default; this host
        // authenticates with JwtBearer (aud=mcp).
        services.AddMcpAuthentication();

        services
            .AddMcpServer()
            // Stateless: each tool-call POST is handled inline within its HTTP request, so the
            // authenticated HttpContext (and thus per-request identity) flows to the tool. With
            // stateful sessions the tool runs on a background loop where HttpContext is null.
            .WithHttpTransport(o => o.Stateless = true)
            // Honors [Authorize]/[AllowAnonymous] on tools, prompts and resources.
            .AddAuthorizationFilters()
            .WithFinanceSentryTools(McpServiceRegistration.McpAssembly);
    }

    public static void UsePipeline(WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapMcpPlatformEndpoints();
        app.MapMcp().RequireAuthorization(AuthPolicies.RequireMcpConnect);
    }
}
