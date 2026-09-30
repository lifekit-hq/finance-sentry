using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Auth.API.Authentication;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinanceSentry.Mcp.Tests;

/// <summary>
/// The MCP HTTP host's authentication pipeline (<see cref="McpHttpHost"/>): stock JwtBearer for
/// <c>aud=mcp</c> bearer tokens, the MCP endpoint behind the mcp.connect permission, a default-deny fallback
/// policy, the platform probes as the only anonymous endpoints, and service tokens honoured only while active
/// and while their account holds mcp.service. Permissions are read per request, so removing one takes effect
/// on the next request without a new token.
/// </summary>
public sealed class McpAuthenticationPipelineTests(McpHttpHostFixture host) : IClassFixture<McpHttpHostFixture>
{
    private const string McpPath = "/";

    /// <summary>Every endpoint that allows anonymous access, as "METHOD route" (* = any method).</summary>
    private static readonly string[] ReviewedAnonymousEndpoints =
    [
        "* /metrics",
        "* /ready",
        "GET /health",
    ];

    [Fact]
    public void AnonymousAccess_IsAllowedOnExactlyTheReviewedEndpoints()
    {
        var endpoints = host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();

        var anonymous = endpoints
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .SelectMany(Describe)
            .Distinct()
            .Order(StringComparer.Ordinal);

        anonymous.Should().Equal(ReviewedAnonymousEndpoints);
    }

    [Fact]
    public void McpEndpoints_AreMapped()
    {
        var routes = host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .SelectMany(Describe);

        routes.Should().Contain($"POST {McpPath}");
    }

    [Fact]
    public async Task FallbackPolicy_DeniesEveryone()
    {
        var fallback = await host.Services.GetRequiredService<IAuthorizationPolicyProvider>().GetFallbackPolicyAsync();

        fallback.Should().BeSameAs(AccessTokenAuthenticationExtensions.DenyAllPolicy);
    }

    [Fact]
    public void EveryMcpEndpoint_RequiresTheMcpConnectPermission()
    {
        var endpoints = host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .ToList();

        endpoints.Should().NotBeEmpty();
        endpoints.Should().OnlyContain(e =>
            e.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(a => a.Policy == AuthPolicies.RequireMcpConnect));
    }

    [Fact]
    public async Task McpRequest_ForMemberWithoutMcpConnect_Returns403()
    {
        var user = await host.CreateUserAsync(AuthRoles.Member);
        var (token, _) = host.Tokens(tokens => tokens.GenerateMcpAccessToken(user));

        var response = await ListToolsAsync(token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task McpRequest_ForMemberGrantedMcpConnect_Returns200()
    {
        var user = await host.CreateUserAsync(AuthRoles.Member, Permissions.McpConnect);
        var (token, _) = host.Tokens(tokens => tokens.GenerateMcpAccessToken(user));

        var response = await ListToolsAsync(token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task McpRequest_AfterMcpConnectIsRemoved_Returns403OnTheNextRequestWithTheSameToken()
    {
        var user = await host.CreateUserAsync(AuthRoles.Member, Permissions.McpConnect);
        var (token, _) = host.Tokens(tokens => tokens.GenerateMcpAccessToken(user));
        (await ListToolsAsync(token)).StatusCode.Should().Be(HttpStatusCode.OK);

        await host.WithUsersAsync(async users =>
            await users.RemoveClaimAsync(
                (await users.FindByIdAsync(user.Id))!, new Claim(Permissions.ClaimType, Permissions.McpConnect)));
        var response = await ListToolsAsync(token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task McpRequest_WithServiceTokenOfAccountWithoutMcpService_Returns401()
    {
        var user = await host.CreateUserAsync(AuthRoles.Member, Permissions.McpConnect);
        var token = await host.IssueServiceTokenAsync(user);

        var response = await ListToolsAsync(token.Token);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Health_WithoutToken_Returns200()
    {
        var response = await host.Client.GetAsync(McpPlatformEndpoints.HealthPath);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task McpRequest_WithoutToken_Returns401WithBearerChallenge()
    {
        var response = await ListToolsAsync(accessToken: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.Should().ContainSingle(h => h.Scheme == "Bearer");
        (await response.Content.ReadFromJsonAsync<ErrorResponseShape>())!.ErrorCode.Should().Be("UNAUTHORIZED");
    }

    [Fact]
    public async Task McpRequest_WithMcpAccessToken_Returns200AndListsTools()
    {
        var user = await host.CreateUserAsync();
        var (token, _) = host.Tokens(tokens => tokens.GenerateMcpAccessToken(user));

        var response = await ListToolsAsync(token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("get_account_summary");
    }

    [Fact]
    public async Task McpRequest_WithAppAudienceToken_Returns401()
    {
        var user = await host.CreateUserAsync();
        var (token, _) = host.Tokens(tokens => tokens.GenerateToken(user, []));

        var response = await ListToolsAsync(token);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync<ErrorResponseShape>())!.ErrorCode.Should().Be("TOKEN_INVALID");
    }

    [Fact]
    public async Task McpRequest_WithTokenWithoutAudience_Returns401()
    {
        var user = await host.CreateUserAsync();

        var response = await ListToolsAsync(McpHttpHostFixture.MintToken(user.Id, audience: null));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task McpRequest_WithTokenForUnknownAccount_Returns401()
    {
        var response = await ListToolsAsync(McpHttpHostFixture.MintToken(Guid.NewGuid().ToString(), AuthAudiences.Mcp));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task McpRequest_WithTokenOfLockedOutAccount_Returns401()
    {
        var user = await host.CreateUserAsync();
        await host.WithUsersAsync(async users =>
            await users.SetLockoutEndDateAsync((await users.FindByIdAsync(user.Id))!, DateTimeOffset.UtcNow.AddHours(1)));
        var (token, _) = host.Tokens(tokens => tokens.GenerateMcpAccessToken(user));

        var response = await ListToolsAsync(token);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task McpRequest_WithActiveServiceToken_Returns200()
    {
        var user = await host.CreateUserAsync();
        var token = await host.IssueServiceTokenAsync(user);

        var response = await ListToolsAsync(token.Token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task McpRequest_WithRevokedServiceToken_Returns401()
    {
        var user = await host.CreateUserAsync();
        var token = await host.IssueServiceTokenAsync(user);
        (await ListToolsAsync(token.Token)).StatusCode.Should().Be(HttpStatusCode.OK);

        await host.WithServiceTokenStoreAsync(store => store.RevokeAsync(token.Jti));
        var response = await ListToolsAsync(token.Token);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task McpRequest_WithServiceTokenNeverStored_Returns401()
    {
        var user = await host.CreateUserAsync();
        var (token, _, _) = host.Tokens(tokens => tokens.GenerateMcpServiceToken(user, lifetimeDays: 1));

        var response = await ListToolsAsync(token);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<HttpResponseMessage> ListToolsAsync(string? accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, McpPath)
        {
            Content = new StringContent(
                """{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (accessToken is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return await host.Client.SendAsync(request);
    }

    private static IEnumerable<string> Describe(RouteEndpoint endpoint)
    {
        var route = "/" + endpoint.RoutePattern.RawText!.TrimStart('/');
        var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
        return methods is { Count: > 0 } ? methods.Select(m => $"{m} {route}") : [$"* {route}"];
    }

    private sealed record ErrorResponseShape(string Error, string ErrorCode);
}
