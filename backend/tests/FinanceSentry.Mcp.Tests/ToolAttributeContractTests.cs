using System.Reflection;
using FinanceSentry.Core.Auth;
using FinanceSentry.Mcp.Tools;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;
using Xunit;

namespace FinanceSentry.Mcp.Tests;

public sealed class ToolAttributeContractTests
{
    [Fact]
    public void RepresentativeTools_Have_McpServerToolTypeAttribute()
    {
        typeof(GetAccountSummaryTool).Should().BeDecoratedWith<McpServerToolTypeAttribute>();
        typeof(ListTransactionsTool).Should().BeDecoratedWith<McpServerToolTypeAttribute>();
        typeof(ListActiveAlertsTool).Should().BeDecoratedWith<McpServerToolTypeAttribute>();
    }

    [Fact]
    public void EveryToolType_Exposes_AtLeastOneAnnotatedMethod()
    {
        var invalidToolTypes = McpToolReflection.GetToolTypes()
            .Where(toolType => McpToolReflection.GetToolNames(toolType).Count == 0)
            .Select(toolType => toolType.Name)
            .ToList();

        invalidToolTypes.Should().BeEmpty(
            because: "every MCP tool type should expose at least one method annotated with [McpServerTool]");
    }

    /// <summary>
    /// Policy coverage for tools: the MCP endpoint requires mcp.connect for every tool, so no tool may opt out
    /// with [AllowAnonymous], and any tool-level [Authorize] must name a registered permission policy.
    /// </summary>
    [Fact]
    public void NoTool_OptsOutOfAuthorization_AndEveryToolPolicyIsKnown()
    {
        var members = McpToolReflection.GetToolTypes()
            .SelectMany(type => new MemberInfo[] { type }.Concat(type.GetMethods(BindingFlags.Instance | BindingFlags.Public)))
            .ToList();

        members.Where(m => m.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
            .Select(m => m.Name)
            .Should().BeEmpty();
        members.SelectMany(m => m.GetCustomAttributes<AuthorizeAttribute>())
            .Where(a => a.Policy is not null && !AuthPolicies.PermissionByPolicy.ContainsKey(a.Policy))
            .Select(a => a.Policy)
            .Should().BeEmpty();
    }
}
