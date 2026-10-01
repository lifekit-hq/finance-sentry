namespace FinanceSentry.Tests.Integration.Auth;

using System.Net;
using System.Net.Http.Json;
using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Tests.Integration.Research;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// Role x feature matrix. Over HTTP: an anonymous caller gets 401 on every gated feature, a Member gets 403
/// on the Owner-only ones and through the gate on connections, and an Owner gets through every gate. At the
/// policy level: each role's principal, built by Identity's claims-principal factory, passes exactly the
/// permission policies its role grants.
/// </summary>
public class RoleFeatureMatrixTests(AssetDossierApiFactory factory) : IClassFixture<AssetDossierApiFactory>
{
    private const string Chat = "chat";
    private const string Narrative = "narrative";
    private const string OpsAdmin = "ops-admin";
    private const string ServiceToken = "service-token";
    private const string Disconnect = "disconnect";

    /// <summary>Feature, permission it needs, and the request that reaches it.</summary>
    private static readonly Dictionary<string, (string Permission, HttpMethod Method, string Path)> Features = new()
    {
        [Chat] = (Permissions.AiUse, HttpMethod.Post, "/api/v1/agent/chat"),
        [Narrative] = (Permissions.AiUse, HttpMethod.Get, "/api/v1/research/assets/AAPL/narrative"),
        [OpsAdmin] = (Permissions.OpsAdmin, HttpMethod.Post, "/api/v1/wealth/admin/backfill-net-worth-history"),
        [ServiceToken] = (Permissions.McpService, HttpMethod.Post, "/api/v1/auth/mcp/service-token"),
        [Disconnect] = (Permissions.ConnectionsManage, HttpMethod.Delete, "/api/v1/crypto/binance/disconnect"),
    };

    public static TheoryData<string> AllFeatures() => [.. Features.Keys];

    [Theory]
    [MemberData(nameof(AllFeatures))]
    public async Task Anonymous_Gets401(string feature)
    {
        using var client = factory.CreateClient();

        var response = await SendAsync(client, feature);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(AllFeatures))]
    public async Task Member_Gets403_UnlessTheMemberRoleGrantsTheFeature(string feature)
    {
        using var client = factory.CreateAuthenticatedClient(Guid.NewGuid(), AuthRoles.Member);

        var response = await SendAsync(client, feature);

        if (Permissions.MemberDefaults.Contains(Features[feature].Permission))
            response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized).And.NotBe(HttpStatusCode.Forbidden);
        else
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Owner rows are driven only where the action is inert in the test host and needs nothing beyond the
    /// access token: the admin backfill would run a job and the service-token endpoint also needs a refresh
    /// session, so the Owner's ops.admin and mcp.service cells are proved at the policy level below.
    /// </summary>
    [Theory]
    [InlineData(Chat)]
    [InlineData(Narrative)]
    [InlineData(Disconnect)]
    public async Task Owner_PassesEveryGate(string feature)
    {
        using var client = factory.CreateAuthenticatedClient(Guid.NewGuid(), AuthRoles.Owner);

        var response = await SendAsync(client, feature);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized).And.NotBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(AuthRoles.Owner)]
    [InlineData(AuthRoles.Member)]
    public async Task EachRole_PassesExactlyThePermissionPoliciesItGrants(string role)
    {
        var userId = Guid.NewGuid();
        TestUsers.EnsureExists(factory.Services, userId, role);
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var principal = await scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>()
            .CreateAsync((await users.FindByIdAsync(userId.ToString()))!);
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();

        var passed = new List<string>();
        foreach (var (policy, permission) in AuthPolicies.PermissionByPolicy)
        {
            if ((await authorization.AuthorizeAsync(principal, policy)).Succeeded)
                passed.Add(permission);
        }

        passed.Distinct().Should().BeEquivalentTo(Permissions.ByRole[role]);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string feature)
    {
        var (_, method, path) = Features[feature];
        using var request = new HttpRequestMessage(method, path);
        if (method == HttpMethod.Post)
            request.Content = JsonContent.Create(new { message = "hello", label = "matrix", lifetimeDays = 1 });
        return await client.SendAsync(request);
    }
}
