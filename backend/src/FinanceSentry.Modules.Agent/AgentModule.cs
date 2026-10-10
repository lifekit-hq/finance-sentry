namespace FinanceSentry.Modules.Agent;

using System.Reflection;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Mcp;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Agent.Application.Services;
using FinanceSentry.Modules.Agent.Domain;
using FinanceSentry.Modules.Agent.Infrastructure;
using FinanceSentry.Modules.Agent.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using FinanceSentry.Infrastructure.Persistence;

/// <summary>
/// Registers the in-app finance agent (feature 040): the Anthropic LLM client, the MCP-tool bridge, the
/// persona composer, the conversation loop, persistence, and the CQRS commands/queries. It also brings
/// the existing MCP tool surface into this host (tool types + identity chain) so the bridge can dispatch
/// tools in the authenticated request scope. The migration is applied centrally by
/// <c>MigrateAllModules</c> on startup, alongside every other module context.
/// </summary>
public static class AgentModule
{
    internal sealed class ModuleRegistrar : IModuleRegistrar
    {
        public void Register(IServiceCollection services, IConfiguration config)
            => services.AddAgentModule(config);
    }

    public static IServiceCollection AddAgentModule(this IServiceCollection services, IConfiguration config)
    {
        services.AddUpdatedAtStamping();
        services.AddDbContext<AgentDbContext>(
            (sp, o) => o.UseNpgsql(
                config.GetConnectionString("Default")!,
                b => b.MigrationsHistoryTable("__ef_migrations_history_agent", "public")).UseUpdatedAtStamping(sp));

        services.Configure<AgentOptions>(config.GetSection(AgentOptions.SectionName));

        services.AddScoped<IConversationRepository, ConversationRepository>();
        services.AddScoped<ILlmClient, AnthropicLlmClient>();
        services.AddSingleton<McpToolBridge>();
        services.AddSingleton<PersonaComposer>();

        var agentOptions = config.GetSection(AgentOptions.SectionName).Get<AgentOptions>() ?? new AgentOptions();

        // Two interchangeable brains (040), resolved from config at startup. OpenClaw delegates the turn
        // to the existing Ledger agent (no Anthropic key); Anthropic runs FS's own persona + tool loop.
        if (agentOptions.Provider == AgentProvider.OpenClaw)
        {
            services.AddScoped<IAgentConversationService, OpenClawAgentConversationService>();
            services.AddHttpClient(OpenClawAgentConversationService.HttpClientName, client =>
            {
                client.BaseAddress = new Uri(agentOptions.OpenClaw.BaseUrl!.TrimEnd('/') + "/");
                client.Timeout = TimeSpan.FromMinutes(5);
            });
        }
        else
        {
            services.AddScoped<IAgentConversationService, AgentConversationService>();
        }

        services.AddHttpClient(AnthropicLlmClient.HttpClientName, client =>
        {
            client.BaseAddress = new Uri(agentOptions.Anthropic.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromMinutes(5);
        });

        RegisterMcpToolSurface(services);

        return services;
    }

    /// <summary>
    /// Brings the existing MCP tool surface into this host so the bridge can dispatch tools in the
    /// authenticated request scope. Registers the identity chain (HTTP-context-based) and every
    /// <c>[McpServerToolType]</c> as scoped — the same set <c>McpServiceRegistration.RegisterShared</c>
    /// wires in the MCP host, minus the module CQRS the API host already registers.
    /// </summary>
    private static void RegisterMcpToolSurface(IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton<LocalMcpCredentialStore>();
        services.AddSingleton<McpOAuthTokenClient>();
        services.AddSingleton<LocalMcpSession>();
        services.AddSingleton<IIdentityResolver, TransportAwareIdentityResolver>();

        foreach (var toolType in McpServiceRegistration.McpAssembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false }
                && t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null))
        {
            services.AddScoped(toolType);
        }
    }
}
