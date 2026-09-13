using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace FinanceSentry.Mcp.Tests.ContractTests;

/// <summary>
/// Builds the exact MCP service graph the host runs (<see cref="McpServiceRegistration"/>) and constructs
/// every registered tool from it. A missing handler registration — the class of bug that made
/// <c>get_pending_companion_events</c> return a 500 before #297 (Companion absent from the module list) —
/// fails here deterministically instead of at runtime. Construction only: no DB connection is opened.
/// </summary>
public sealed class ToolResolutionTests
{
    /// <summary>
    /// The MCP host's configuration as production runs it: the connection string and the one key the
    /// module registrars demand at registration time — and NOTHING the worker role holds. No
    /// <c>Encryption:Keys</c>: <c>docker-compose.prod.yml</c> wires those into the API service only.
    /// </summary>
    private static ServiceProvider BuildMcpGraph(string environmentName = "Production")
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Host=localhost;Port=5432;Database=x;Username=y;Password=z",
                ["ConnectionStrings:ReadOnly"] = "Host=localhost;Port=5432;Database=x;Username=y;Password=z",
                // Minimal config the module registrars validate at registration time.
                ["Deduplication:MasterKeyBase64"] = "MDEyMzQ1Njc4OUFCQ0RFRg==",
            })
            .Build();

        // The host builder registers IConfiguration and IHostEnvironment automatically; mirror that so
        // services that inject them (e.g. LocalMcpCredentialStore, options validators) resolve exactly
        // as they do at runtime.
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<IHostEnvironment>(new StubHostEnvironment(environmentName));
        McpServiceRegistration.RegisterShared(services, config);

        return services.BuildServiceProvider(validateScopes: true);
    }

    [Fact]
    public void EveryTool_ConstructsFromTheRealServiceGraph()
    {
        using var provider = BuildMcpGraph();
        using var scope = provider.CreateScope();

        var toolTypes = McpToolReflection.GetToolTypes();
        toolTypes.Should().NotBeEmpty("the MCP assembly must expose tool types");

        var failures = new List<string>();
        foreach (var toolType in toolTypes)
        {
            try
            {
                _ = scope.ServiceProvider.GetRequiredService(toolType);
            }
            catch (Exception ex)
            {
                failures.Add($"{toolType.Name}: {ex.GetType().Name} — {ex.Message}");
            }
        }

        failures.Should().BeEmpty(
            "every MCP tool must construct from the wired graph — an unresolved handler is the #297 bug class");
    }

    /// <summary>
    /// Issue #613: <c>ValidateOnStart()</c> registrations run when the HOST starts, not when the
    /// container is built — so the tool-construction test above passed on a graph whose process died
    /// on boot. After #609 the MCP crash-looped for three days in production because BankSync's
    /// module registrar carried the API's <c>Encryption:Keys</c> validation into every host. This runs
    /// the same startup validations the host runs, in Production, with no worker-role secrets: a
    /// validation that needs something the MCP host does not hold belongs in an
    /// <c>IWorkerRegistrar</c>, and this test is where that mistake surfaces next time.
    /// </summary>
    [Fact]
    public void McpGraph_PassesStartupValidation_WithoutTheWorkerRoleSecrets()
    {
        using var provider = BuildMcpGraph(Environments.Production);

        // IStartupValidator is what Host.StartAsync invokes for every ValidateOnStart() option; null
        // means the graph registered no startup validations at all, which also passes.
        var startupValidator = provider.GetService<IStartupValidator>();

        var act = () => startupValidator?.Validate();

        act.Should().NotThrow(
            "the MCP host starts with the production compose environment, which holds no worker-role "
            + "secrets — a startup validation that needs one is the #613 crash loop");
    }

    private sealed class StubHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "FinanceSentry.Mcp.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
