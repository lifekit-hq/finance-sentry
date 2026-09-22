using System.Reflection;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Integration;
using FinanceSentry.Mcp.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp;

/// <summary>
/// The shared MCP service graph — the canonical module list, CQRS handlers, identity, and every tool
/// type. Extracted from <c>Program.cs</c> so tests build the exact same graph the host runs (feature
/// 035): a DI-resolution test constructs every tool from this graph, turning a missing handler
/// registration (the class of bug fixed in #297) into a deterministic failure instead of a runtime 500.
///
/// <see cref="ModuleAssemblies"/> is the single source of truth for which modules the MCP host loads —
/// add a module here and both the host and the resolution test pick it up.
/// </summary>
public static class McpServiceRegistration
{
    public static readonly Assembly[] ModuleAssemblies =
    [
        typeof(FinanceSentry.Modules.Alerts.AlertsModule).Assembly,
        typeof(FinanceSentry.Modules.Auth.AuthModule).Assembly,
        typeof(FinanceSentry.Modules.BankSync.BankSyncModule).Assembly,
        typeof(FinanceSentry.Modules.BrokerageSync.BrokerageSyncModule).Assembly,
        typeof(FinanceSentry.Modules.Budgets.BudgetsModule).Assembly,
        typeof(FinanceSentry.Modules.CryptoSync.CryptoSyncModule).Assembly,
        typeof(FinanceSentry.Modules.Subscriptions.SubscriptionsModule).Assembly,
        typeof(FinanceSentry.Modules.Wealth.WealthModule).Assembly,
        typeof(FinanceSentry.Modules.Research.ResearchModule).Assembly,
        typeof(FinanceSentry.Modules.Radar.RadarModule).Assembly,
        typeof(FinanceSentry.Modules.Risk.RiskModule).Assembly,
        typeof(FinanceSentry.Modules.Companion.CompanionModule).Assembly,
        typeof(FinanceSentry.Modules.Analytics.AnalyticsModule).Assembly,
        typeof(FinanceSentry.Modules.Events.EventsModule).Assembly,
    ];

    public static readonly Assembly McpAssembly = typeof(Tools.GetAccountSummaryTool).Assembly;

    public static void RegisterShared(IServiceCollection services, IConfiguration config)
    {
        services.AddCqrs([.. ModuleAssemblies, McpAssembly]);

        var registrarType = typeof(IModuleRegistrar);
        foreach (var assembly in ModuleAssemblies)
        {
            foreach (var implType in assembly.GetTypes()
                .Where(t => !t.IsInterface && !t.IsAbstract && registrarType.IsAssignableFrom(t)))
            {
                var registrar = (IModuleRegistrar)Activator.CreateInstance(implType)!;
                registrar.Register(services, config);
            }
        }

        // 039: the cross-module read-port adapters (IPS↔Risk) live in the shared FinanceSentry.Integration
        // composition lib so every host — API and this MCP host — registers them. Without this the MCP host
        // leaves IAllocationPolicySource / IPositionCapSource unregistered and risk/allocation tools throw.
        services.AddCrossModulePorts();

        services.AddHttpContextAccessor();
        services.AddSingleton<LocalMcpCredentialStore>();
        services.AddSingleton<McpOAuthTokenClient>();
        services.AddSingleton<LocalMcpSession>();
        services.AddSingleton<IIdentityResolver, TransportAwareIdentityResolver>();

        foreach (var toolType in McpAssembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface && t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null))
        {
            services.AddScoped(toolType);
        }
    }
}
