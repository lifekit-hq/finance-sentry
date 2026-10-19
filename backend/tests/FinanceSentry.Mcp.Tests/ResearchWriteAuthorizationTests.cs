using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using FinanceSentry.Core.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;
using Xunit;

namespace FinanceSentry.Mcp.Tests;

/// <summary>
/// A service token (the headless agent, Ledger) never creates, changes or deletes Research data: the Research write
/// tools carry <see cref="AuthPolicies.RequireMcpPersonalToken"/>, so they are hidden from its tools/list and refused
/// on call, and the watchlist tool refuses add/remove while list stays open. The owner's own session (a personal
/// token) keeps every one of them. Proposal tools (propose_recommendation, score_candidate) stay open to the agent.
/// </summary>
public sealed class ResearchWriteAuthorizationTests(McpHttpHostFixture host) : IClassFixture<McpHttpHostFixture>
{
    private const string McpPath = "/";

    private static readonly string[] PersonalTokenOnlyTools =
    [
        "delete_thesis",
        "promote_candidate",
        "record_risk_remeasurement",
        "register_thesis_source",
        "reject_candidate",
        "save_ips",
        "save_thesis",
    ];

    public static TheoryData<string> PersonalTokenOnlyToolNames() => [.. PersonalTokenOnlyTools];

    [Fact]
    public void PersonalTokenGate_CoversExactlyTheResearchWriteTools()
    {
        var gated = McpToolReflection.GetToolTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            .Where(method => method.GetCustomAttributes<AuthorizeAttribute>()
                .Any(a => a.Policy == AuthPolicies.RequireMcpPersonalToken))
            .Select(method => method.GetCustomAttribute<McpServerToolAttribute>()!.Name)
            .Order(StringComparer.Ordinal);

        gated.Should().Equal(PersonalTokenOnlyTools);
    }

    [Fact]
    public async Task ToolsList_ForServiceToken_HidesTheResearchWriteTools_AndKeepsReadsAndProposals()
    {
        var token = await ServiceTokenAsync();

        var body = await ReadBodyAsync(await PostAsync(token, """{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}"""));

        foreach (var tool in PersonalTokenOnlyTools)
            body.Should().NotContain($"\"{tool}\"");
        body.Should().Contain("\"list_theses\"").And.Contain("\"propose_recommendation\"")
            .And.Contain("\"score_candidate\"").And.Contain("\"watchlist\"");
    }

    [Fact]
    public async Task ToolsList_ForPersonalToken_ListsTheResearchWriteTools()
    {
        var token = PersonalToken(await host.CreateUserAsync());

        var body = await ReadBodyAsync(await PostAsync(token, """{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}"""));

        foreach (var tool in PersonalTokenOnlyTools)
            body.Should().Contain($"\"{tool}\"");
    }

    [Theory]
    [MemberData(nameof(PersonalTokenOnlyToolNames))]
    public async Task CallingAResearchWriteTool_WithServiceToken_IsRefused(string tool)
    {
        var token = await ServiceTokenAsync();

        var body = await ReadBodyAsync(await CallToolAsync(token, tool, "{}"));

        body.Should().Contain("\"error\"").And.Contain("forbidden", Exactly.Once(), "the authorization filter refuses before the tool runs");
    }

    [Theory]
    [InlineData("""{"action":"add","ticker":"NVDA"}""")]
    [InlineData("""{"action":"remove","itemId":"6f1c4f2e-8d0a-4a5b-9a51-2f7c3f6f8b11"}""")]
    public async Task Watchlist_AddOrRemove_WithServiceToken_IsRefused(string arguments)
    {
        var token = await ServiceTokenAsync();

        var body = await ReadBodyAsync(await CallToolAsync(token, "watchlist", arguments));

        body.Should().Contain("a service token can only list the watchlist");
    }

    [Fact]
    public async Task Watchlist_Add_WithPersonalToken_PassesTheGate()
    {
        var token = PersonalToken(await host.CreateUserAsync());

        var body = await ReadBodyAsync(await CallToolAsync(token, "watchlist", """{"action":"add"}"""));

        body.Should().Contain("action=add requires a ticker").And.NotContain("a service token can only list");
    }

    /// <summary>An Owner holds mcp.service, as Ledger's account does; the service token is what marks the agent.</summary>
    private async Task<string> ServiceTokenAsync() => (await host.IssueServiceTokenAsync(await host.CreateUserAsync())).Token;

    private string PersonalToken(Modules.Auth.Domain.Entities.ApplicationUser user) =>
        host.Tokens(tokens => tokens.GenerateMcpAccessToken(user)).Item1;

    private Task<HttpResponseMessage> CallToolAsync(string accessToken, string tool, string arguments) =>
        PostAsync(accessToken,
            $$"""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"{{tool}}","arguments":""" + arguments + "}}");

    private async Task<HttpResponseMessage> PostAsync(string accessToken, string json)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, McpPath)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return await host.Client.SendAsync(request);
    }

    private static async Task<string> ReadBodyAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }
}
