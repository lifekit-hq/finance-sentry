using FinanceSentry.Core.Connections;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FinanceSentry.Mcp.Tests;

/// <summary>
/// The MCP host reads connection health for <c>get_sync_health</c> through the same switch the API's sync paths and
/// Accounts page use (<c>ConnectionHealth:ActiveProviders</c>), so its committed list must be the API's: a provider
/// flipped in one and not the other would read as policy-driven in the app and as shadow to the agent.
/// </summary>
public sealed class ConnectionHealthConfigParityTests
{
    private static string[] ActiveProviders(string host)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FinanceSentry.sln")))
            dir = dir.Parent;
        dir.Should().NotBeNull("the tests run inside the backend tree");

        var options = new ConnectionHealthOptions();
        new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(dir!.FullName, "src", host, "appsettings.json"), optional: false)
            .Build()
            .GetSection(ConnectionHealthOptions.SectionName)
            .Bind(options);
        return [.. options.ActiveProviders];
    }

    [Fact]
    public void The_MCP_host_lists_the_same_active_providers_as_the_API() =>
        ActiveProviders("FinanceSentry.Mcp").Should().BeEquivalentTo(ActiveProviders("FinanceSentry.API"));
}
