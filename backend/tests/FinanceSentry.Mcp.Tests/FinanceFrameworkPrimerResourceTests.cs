using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FinanceSentry.Core.Auth;
using FinanceSentry.Mcp.Resources;
using FluentAssertions;
using Xunit;

namespace FinanceSentry.Mcp.Tests;

/// <summary>
/// The HTTP host advertises the framework primer and serves it whole over <c>resources/read</c>, the
/// call an MCP client's resource utility (e.g. OpenClaw's <c>resources_read</c>) makes.
/// </summary>
public sealed class FinanceFrameworkPrimerResourceTests(McpHttpHostFixture host) : IClassFixture<McpHttpHostFixture>
{
    [Fact]
    public async Task ResourcesList_AdvertisesThePrimer()
    {
        var body = await PostAsync("""{"jsonrpc":"2.0","id":1,"method":"resources/list","params":{}}""");

        body.Should().Contain($"\"uri\":\"{FinanceFrameworkPrimerResource.Uri}\"")
            .And.Contain("\"name\":\"finance_framework_primer\"")
            .And.Contain("\"mimeType\":\"text/markdown\"");
    }

    [Fact]
    public async Task ResourcesRead_ReturnsThePrimerText()
    {
        var body = await PostAsync(
            $$$"""{"jsonrpc":"2.0","id":1,"method":"resources/read","params":{"uri":"{{{FinanceFrameworkPrimerResource.Uri}}}"}}""");

        var contents = Result(body).GetProperty("contents");
        contents.GetArrayLength().Should().Be(1);
        contents[0].GetProperty("uri").GetString().Should().Be(FinanceFrameworkPrimerResource.Uri);
        contents[0].GetProperty("mimeType").GetString().Should().Be("text/markdown");
        contents[0].GetProperty("text").GetString().Should().Be(FinanceFrameworkPrimerResource.Read())
            .And.StartWith("# Finance Sentry framework primer");
    }

    /// <summary>The JSON-RPC <c>result</c>, from a plain JSON body or the SSE <c>data:</c> line.</summary>
    private static JsonElement Result(string body)
    {
        var json = body.Split('\n').Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith("data:", StringComparison.Ordinal))?["data:".Length..] ?? body;
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("result").Clone();
    }

    private async Task<string> PostAsync(string json)
    {
        var user = await host.CreateUserAsync(AuthRoles.Member, Permissions.McpConnect);
        var (token, _) = host.Tokens(tokens => tokens.GenerateMcpAccessToken(user));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await host.Client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }
}
