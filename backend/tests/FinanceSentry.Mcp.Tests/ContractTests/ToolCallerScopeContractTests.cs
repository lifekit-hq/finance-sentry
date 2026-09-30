using System.Reflection;
using FinanceSentry.Mcp.Middleware;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using Xunit;

namespace FinanceSentry.Mcp.Tests.ContractTests;

/// <summary>
/// Every tool acts for the authenticated MCP identity only, so no tool may accept the user to act
/// for as an argument — neither on the advertised input schema nor on the bound .NET signature.
/// </summary>
public sealed class ToolCallerScopeContractTests
{
    private static readonly string[] UserIdentifierNames = ["userid", "user_id"];

    private static bool IsUserIdentifier(string name)
        => UserIdentifierNames.Contains(name.ToLowerInvariant());

    [Fact]
    public void NoToolMethod_DeclaresAUserIdParameter()
    {
        var offenders = McpToolReflection.GetToolTypes()
            .SelectMany(toolType => toolType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null)
                .SelectMany(method => method.GetParameters()
                    .Where(parameter => parameter.Name is not null && IsUserIdentifier(parameter.Name))
                    .Select(parameter => $"{toolType.Name}.{method.Name}({parameter.Name})")))
            .ToList();

        offenders.Should().BeEmpty(
            because: "tools scope to the authenticated identity; the acting user is never a tool argument");
    }

    [Fact]
    public void NoRegisteredTool_AdvertisesAUserIdInItsInputSchema()
    {
        var services = new ServiceCollection();
        services.AddMcpServer().WithFinanceSentryTools(McpServiceRegistration.McpAssembly);
        var tools = services.BuildServiceProvider()
            .GetRequiredService<IOptions<McpServerOptions>>()
            .Value.ToolCollection;

        tools.Should().NotBeNullOrEmpty();

        var offenders = tools!
            .Where(tool => tool.ProtocolTool.InputSchema.TryGetProperty("properties", out var properties)
                && properties.EnumerateObject().Any(property => IsUserIdentifier(property.Name)))
            .Select(tool => tool.ProtocolTool.Name)
            .ToList();

        offenders.Should().BeEmpty(
            because: "an MCP client must not be offered a way to choose whose data a tool reads or writes");
    }
}
