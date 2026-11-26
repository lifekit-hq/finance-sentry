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
/// The split between what Ledger's service token may write and what stays the owner's. Ledger rejects candidates, keeps
/// the watchlist and PROPOSES thesis changes (a promotion, a narrative or trigger update, a close); it never writes a
/// thesis itself. Owner-session only, hidden from a service token's tools/list and refused on call: saving, promoting,
/// reverting and deleting a thesis, confirming or rejecting a proposal, registering a thesis source, and anything on
/// the IPS or the risk measurement behind it. The owner's own session (a personal token) keeps every tool.
/// </summary>
public sealed class ResearchWriteAuthorizationTests(McpHttpHostFixture host) : IClassFixture<McpHttpHostFixture>
{
    private const string McpPath = "/";

    private static readonly string[] PersonalTokenOnlyTools =
    [
        "decide_thesis_revision",
        "delete_thesis",
        "promote_candidate",
        "record_risk_remeasurement",
        "register_thesis_source",
        "revert_thesis_revision",
        "save_ips",
        "save_thesis",
    ];

    /// <summary>The thesis writes Ledger's service token must never reach: each one changes a thesis or promotes a candidate.</summary>
    private static readonly string[] ThesisWriteTools =
    [
        "decide_thesis_revision",
        "promote_candidate",
        "revert_thesis_revision",
        "save_thesis",
    ];

    /// <summary>What Ledger's service token may call: the candidate and watchlist writes the thesis proposal and the no-change thesis review.</summary>
    private static readonly string[] LedgerWritableTools =
    [
        "propose_thesis_revision",
        "record_thesis_review",
        "reject_candidate",
        "watchlist",
    ];

    public static TheoryData<string> PersonalTokenOnlyToolNames() => [.. PersonalTokenOnlyTools];

    public static TheoryData<string> LedgerWritableToolNames() => [.. LedgerWritableTools];

    public static TheoryData<string> ThesisWriteToolNames() => [.. ThesisWriteTools];

    [Fact]
    public void PersonalTokenGate_CoversExactlyTheOwnerOnlyTools()
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
    public async Task ToolsList_ForServiceToken_HidesTheOwnerOnlyTools_AndListsWhatLedgerMayWrite()
    {
        var token = await ServiceTokenAsync();

        var body = await ReadBodyAsync(await PostAsync(token, """{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}"""));

        foreach (var tool in PersonalTokenOnlyTools)
            body.Should().NotContain($"\"{tool}\"");
        foreach (var tool in LedgerWritableTools)
            body.Should().Contain($"\"{tool}\"");
        body.Should().Contain("\"list_theses\"").And.Contain("\"list_thesis_events\"")
            .And.Contain("\"propose_recommendation\"").And.Contain("\"score_candidate\"")
            .And.Contain("\"list_thesis_revisions\"");
    }

    [Fact]
    public async Task ToolsList_ForPersonalToken_ListsEveryResearchWriteTool()
    {
        var token = PersonalToken(await host.CreateUserAsync());

        var body = await ReadBodyAsync(await PostAsync(token, """{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}"""));

        foreach (var tool in PersonalTokenOnlyTools.Concat(LedgerWritableTools))
            body.Should().Contain($"\"{tool}\"");
    }

    [Theory]
    [MemberData(nameof(PersonalTokenOnlyToolNames))]
    public async Task CallingAnOwnerOnlyTool_WithServiceToken_IsRefused(string tool)
    {
        var token = await ServiceTokenAsync();

        var body = await ReadBodyAsync(await CallToolAsync(token, tool, "{}"));

        body.Should().Contain("\"error\"").And.Contain("forbidden", Exactly.Once(), "the authorization filter refuses before the tool runs");
    }

    [Theory]
    [MemberData(nameof(ThesisWriteToolNames))]
    public async Task ToolsList_ForServiceToken_DoesNotOfferAnyThesisWrite(string tool)
    {
        var token = await ServiceTokenAsync();

        var body = await ReadBodyAsync(await PostAsync(token, """{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}"""));

        body.Should().NotContain($"\"{tool}\"");
    }

    [Theory]
    [MemberData(nameof(ThesisWriteToolNames))]
    public async Task CallingAThesisWrite_WithServiceTokenAndRealArguments_IsRefusedBeforeTheToolRuns(string tool)
    {
        var token = await ServiceTokenAsync();
        var arguments = $$"""{"id":"{{Guid.NewGuid()}}","eventId":"{{Guid.NewGuid()}}","verdict":"Confirm"}""";

        var body = await ReadBodyAsync(await CallToolAsync(token, tool, arguments));

        body.Should().Contain("\"error\"").And.Contain("forbidden");
    }

    [Theory]
    [MemberData(nameof(LedgerWritableToolNames))]
    public async Task CallingALedgerWritableTool_WithServiceToken_PassesTheAuthorizationGate(string tool)
    {
        var token = await ServiceTokenAsync();

        var body = await ReadBodyAsync(await CallToolAsync(token, tool, "{}"));

        body.Should().NotContain("forbidden", "no policy stands between the service token and the tool");
    }

    [Theory]
    [InlineData("""{"action":"add"}""", "action=add requires a ticker")]
    [InlineData("""{"action":"remove"}""", "action=remove requires an itemId")]
    public async Task Watchlist_AddOrRemove_WithServiceToken_ReachesTheTool(string arguments, string expected)
    {
        var token = await ServiceTokenAsync();

        var body = await ReadBodyAsync(await CallToolAsync(token, "watchlist", arguments));

        body.Should().Contain(expected).And.NotContain("a service token can only list");
    }

    [Fact]
    public async Task Watchlist_Add_WithPersonalToken_PassesTheGate()
    {
        var token = PersonalToken(await host.CreateUserAsync());

        var body = await ReadBodyAsync(await CallToolAsync(token, "watchlist", """{"action":"add"}"""));

        body.Should().Contain("action=add requires a ticker");
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
