namespace FinanceSentry.Tests.Integration.Auth;

using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Auth.Application.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// Revoking a person from the People page locks the account out, removes its roles and per-person grants, and
/// revokes its refresh and MCP service tokens. Each request reloads the principal, so a revoke (or a removed
/// permission) applies to the very next request without anyone signing in again.
/// </summary>
public class PeopleRevocationTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private const string Password = "TestPass123!";
    private const int ServiceTokenLifetimeDays = 30;

    [Fact]
    public async Task Revoke_LocksTheAccountOut_ItsLiveAccessTokenGets401()
    {
        using var owner = await OwnerClientAsync("revoke-owner-lockout@test.com");
        var (memberId, memberToken) = await MemberAsync("revoke-member-lockout@test.com");
        using var member = factory.CookieClient(("fs_access_token", memberToken));
        (await member.GetAsync("/api/v1/profile")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await owner.PostAsync($"/api/v1/people/{memberId}/revoke", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await member.GetAsync("/api/v1/profile")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await factory.CookieClient().PostAsJsonAsync("/api/v1/auth/login",
            new { email = "revoke-member-lockout@test.com", password = Password })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Revoke_RevokesRefreshAndMcpServiceTokens_AndRemovesRolesAndGrants()
    {
        using var owner = await OwnerClientAsync("revoke-owner-tokens@test.com");
        await factory.EnsureUserExistsAsync("revoke-member-tokens@test.com", Password);
        var login = await factory.CookieClient().PostAsJsonAsync("/api/v1/auth/login",
            new { email = "revoke-member-tokens@test.com", password = Password });
        var refreshToken = AuthApiFactory.CookieValue(login, "fs_refresh_token");
        var memberId = await UserIdAsync("revoke-member-tokens@test.com");
        var serviceTokenId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IMcpServiceTokenStore>().AddAsync(
                new McpServiceToken(serviceTokenId, memberId, "headless client", DateTime.UtcNow.AddDays(ServiceTokenLifetimeDays)));
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            await users.AddClaimAsync((await users.FindByIdAsync(memberId))!, new Claim(Permissions.ClaimType, Permissions.AiUse));
        }

        (await owner.PostAsync($"/api/v1/people/{memberId}/revoke", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await factory.CookieClient(("fs_refresh_token", refreshToken)).PostAsync("/api/v1/auth/refresh", null))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var check = factory.Services.CreateScope();
        (await check.ServiceProvider.GetRequiredService<IMcpServiceTokenStore>().IsActiveAsync(serviceTokenId))
            .Should().BeFalse();
        var userManager = check.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var member = (await userManager.FindByIdAsync(memberId))!;
        (await userManager.GetRolesAsync(member)).Should().BeEmpty();
        (await userManager.GetClaimsAsync(member)).Should().NotContain(c => c.Type == Permissions.ClaimType);

        var listed = await owner.GetFromJsonAsync<IReadOnlyList<PersonShape>>("/api/v1/people");
        listed!.Single(p => p.Id == memberId).Status.Should().Be("Revoked");
    }

    [Fact]
    public async Task Revoke_OfAPendingInvite_VoidsTheInviteLink()
    {
        using var owner = await OwnerClientAsync("revoke-owner-pending@test.com");
        var (userId, token) = await factory.CreatePendingInviteAsync("revoke-pending@test.com");

        (await owner.PostAsync($"/api/v1/people/{userId}/revoke", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await PeopleInviteContractTests.ShouldBeInvalidInviteAsync(await factory.CookieClient().PostAsJsonAsync(
            "/api/v1/auth/invite/accept", new { userId, token, password = "quiet lantern orchard" }));
    }

    [Fact]
    public async Task Revoke_Self_Returns400()
    {
        using var owner = await OwnerClientAsync("revoke-owner-self@test.com");
        var ownerId = await UserIdAsync("revoke-owner-self@test.com");

        var response = await owner.PostAsync($"/api/v1/people/{ownerId}/revoke", null);

        await ShouldFailAsync(response, HttpStatusCode.BadRequest, "CANNOT_REVOKE_SELF");
    }

    [Fact]
    public async Task Revoke_AnOwner_Returns400()
    {
        using var owner = await OwnerClientAsync("revoke-owner-a@test.com");
        using var _ = await OwnerClientAsync("revoke-owner-b@test.com");

        var response = await owner.PostAsync($"/api/v1/people/{await UserIdAsync("revoke-owner-b@test.com")}/revoke", null);

        await ShouldFailAsync(response, HttpStatusCode.BadRequest, "CANNOT_REVOKE_OWNER");
    }

    [Fact]
    public async Task Revoke_UnknownPerson_Returns404()
    {
        using var owner = await OwnerClientAsync("revoke-owner-unknown@test.com");

        var response = await owner.PostAsync($"/api/v1/people/{Guid.NewGuid()}/revoke", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RemovingAPermission_Returns403OnTheNextRequest_WithoutSigningInAgain()
    {
        var (memberId, memberToken) = await MemberAsync("revoke-member-grant@test.com");
        var grant = new Claim(Permissions.ClaimType, Permissions.UsersManage);
        await ChangeUserAsync(memberId, (users, user) => users.AddClaimAsync(user, grant));
        using var member = factory.CookieClient(("fs_access_token", memberToken));
        (await member.GetAsync("/api/v1/people")).StatusCode.Should().Be(HttpStatusCode.OK);

        await ChangeUserAsync(memberId, (users, user) => users.RemoveClaimAsync(user, grant));

        (await member.GetAsync("/api/v1/people")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.GetAsync("/api/v1/profile")).StatusCode.Should().Be(HttpStatusCode.OK, "the account itself is still active");
    }

    private async Task<HttpClient> OwnerClientAsync(string email)
    {
        await factory.EnsureUserExistsAsync(email, Password);
        using (var scope = factory.Services.CreateScope())
            TestUsers.GrantRole(scope.ServiceProvider, await UserIdAsync(email), AuthRoles.Owner);

        return factory.CookieClient(("fs_access_token", await factory.SignInAsync(email, Password)));
    }

    private async Task<(string UserId, string AccessToken)> MemberAsync(string email)
    {
        await factory.EnsureUserExistsAsync(email, Password);
        return (await UserIdAsync(email), await factory.SignInAsync(email, Password));
    }

    private async Task<string> UserIdAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email))!.Id;
    }

    private async Task ChangeUserAsync(string userId, Func<UserManager<ApplicationUser>, ApplicationUser, Task<IdentityResult>> change)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        (await change(users, (await users.FindByIdAsync(userId))!)).Succeeded.Should().BeTrue();
    }

    private static async Task ShouldFailAsync(HttpResponseMessage response, HttpStatusCode status, string errorCode)
    {
        response.StatusCode.Should().Be(status);
        (await response.Content.ReadFromJsonAsync<ErrorShape>())!.ErrorCode.Should().Be(errorCode);
    }

    private sealed record PersonShape(string Id, string Email, IReadOnlyList<string> Roles, string Status);

    private sealed record ErrorShape(string Error, string ErrorCode);
}
