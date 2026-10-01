namespace FinanceSentry.Tests.Integration.Auth;

using System.Net;
using System.Net.Http.Json;
using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// Invite-only onboarding: an owner creates an invite on the People page (an account with no password plus a
/// one-time password-reset token), the invited person sets a password with it and is signed in. Self-service
/// registration no longer exists.
/// </summary>
public class PeopleInviteContractTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private const string Password = "TestPass123!";
    private const string NewPassword = "quiet lantern orchard";

    [Fact]
    public async Task CreateInvite_AsOwner_ReturnsOneTimeTokenAndListsThePersonAsInvited()
    {
        using var owner = await OwnerClientAsync("invite-owner-create@test.com");

        var response = await owner.PostAsJsonAsync("/api/v1/people/invites", new { email = "friend-create@test.com" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var invite = (await response.Content.ReadFromJsonAsync<InviteShape>())!;
        invite.Email.Should().Be("friend-create@test.com");
        invite.Token.Should().NotBeNullOrWhiteSpace();
        invite.ExpiresAt.Should().BeAfter(DateTime.UtcNow.AddDays(6));

        var person = (await ListAsync(owner)).Single(p => p.Email == "friend-create@test.com");
        person.Id.Should().Be(invite.UserId);
        person.Status.Should().Be("Invited");
        person.Roles.Should().Equal(AuthRoles.Member);
    }

    [Fact]
    public async Task PeopleEndpoints_AsMember_Return403()
    {
        await factory.EnsureUserExistsAsync("invite-member@test.com", Password);
        using var member = factory.CookieClient(("fs_access_token", await factory.SignInAsync("invite-member@test.com", Password)));

        (await member.GetAsync("/api/v1/people")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.PostAsJsonAsync("/api/v1/people/invites", new { email = "nope@test.com" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.PostAsync($"/api/v1/people/{Guid.NewGuid()}/revoke", null))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AcceptInvite_SetsThePasswordSignsInAndActivatesTheAccount()
    {
        using var owner = await OwnerClientAsync("invite-owner-accept@test.com");
        var invite = await InviteAsync(owner, "friend-accept@test.com");

        var accept = await factory.CookieClient().PostAsJsonAsync("/api/v1/auth/invite/accept",
            new { userId = invite.UserId, token = invite.Token, password = NewPassword });

        accept.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await accept.Content.ReadFromJsonAsync<AuthResponseShape>())!;
        body.User.Id.Should().Be(invite.UserId);
        body.User.Roles.Should().Equal(AuthRoles.Member);
        accept.Headers.GetValues("Set-Cookie").Should().Contain(c => c.StartsWith("fs_access_token="));

        await factory.SignInAsync("friend-accept@test.com", NewPassword);
        (await ListAsync(owner)).Single(p => p.Email == "friend-accept@test.com").Status.Should().Be("Active");
    }

    [Fact]
    public async Task AcceptInvite_UsedTwice_SecondUseReturnsInvalidInvite()
    {
        using var owner = await OwnerClientAsync("invite-owner-once@test.com");
        var invite = await InviteAsync(owner, "friend-once@test.com");
        using var anonymous = factory.CookieClient();

        (await anonymous.PostAsJsonAsync("/api/v1/auth/invite/accept",
            new { userId = invite.UserId, token = invite.Token, password = NewPassword })).StatusCode.Should().Be(HttpStatusCode.OK);

        var again = await anonymous.PostAsJsonAsync("/api/v1/auth/invite/accept",
            new { userId = invite.UserId, token = invite.Token, password = "another long passphrase" });

        await ShouldBeInvalidInviteAsync(again);
    }

    [Fact]
    public async Task AcceptInvite_WithWrongToken_ReturnsInvalidInvite()
    {
        var (userId, _) = await factory.CreatePendingInviteAsync("friend-wrong-token@test.com");

        var response = await factory.CookieClient().PostAsJsonAsync("/api/v1/auth/invite/accept",
            new { userId, token = "not-a-token", password = NewPassword });

        await ShouldBeInvalidInviteAsync(response);
    }

    [Fact]
    public async Task CreateInvite_ForAPendingEmail_IssuesAFreshTokenAndVoidsTheEarlierOne()
    {
        using var owner = await OwnerClientAsync("invite-owner-reissue@test.com");
        var first = await InviteAsync(owner, "friend-reissue@test.com");
        var second = await InviteAsync(owner, "friend-reissue@test.com");
        second.UserId.Should().Be(first.UserId);
        using var anonymous = factory.CookieClient();

        await ShouldBeInvalidInviteAsync(await anonymous.PostAsJsonAsync("/api/v1/auth/invite/accept",
            new { userId = first.UserId, token = first.Token, password = NewPassword }));
        (await anonymous.PostAsJsonAsync("/api/v1/auth/invite/accept",
            new { userId = second.UserId, token = second.Token, password = NewPassword })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateInvite_ForAnExistingAccount_Returns400DuplicateEmail()
    {
        using var owner = await OwnerClientAsync("invite-owner-dup@test.com");
        await factory.EnsureUserExistsAsync("friend-existing@test.com", Password);

        var response = await owner.PostAsJsonAsync("/api/v1/people/invites", new { email = "friend-existing@test.com" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ErrorShape>())!.ErrorCode.Should().Be("DUPLICATE_EMAIL");
    }

    [Fact]
    public async Task CreateInvite_WithInvalidEmail_Returns400ValidationError()
    {
        using var owner = await OwnerClientAsync("invite-owner-invalid@test.com");

        var response = await owner.PostAsJsonAsync("/api/v1/people/invites", new { email = "not-an-email" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ErrorShape>())!.ErrorCode.Should().Be("VALIDATION_ERROR");
    }

    [Fact]
    public async Task Login_WithAPendingInvite_Returns401InvalidCredentials()
    {
        await factory.CreatePendingInviteAsync("friend-pending-login@test.com");

        var response = await factory.CookieClient().PostAsJsonAsync("/api/v1/auth/login",
            new { email = "friend-pending-login@test.com", password = NewPassword });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync<ErrorShape>())!.ErrorCode.Should().Be("INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task Register_IsRemoved()
    {
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText)
            .Should().NotContain(r => r != null && r.EndsWith("auth/register", StringComparison.OrdinalIgnoreCase));

        // An unmapped route falls under the deny-all fallback policy.
        var response = await factory.CookieClient().PostAsJsonAsync("/api/v1/auth/register",
            new { email = "self-signup@test.com", password = NewPassword });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var scope = factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
            .FindByEmailAsync("self-signup@test.com")).Should().BeNull();
    }

    private async Task<HttpClient> OwnerClientAsync(string email)
    {
        await factory.EnsureUserExistsAsync(email, Password);
        using (var scope = factory.Services.CreateScope())
        {
            var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email);
            TestUsers.GrantRole(scope.ServiceProvider, user!.Id, AuthRoles.Owner);
        }

        return factory.CookieClient(("fs_access_token", await factory.SignInAsync(email, Password)));
    }

    private static async Task<InviteShape> InviteAsync(HttpClient owner, string email)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/people/invites", new { email });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<InviteShape>())!;
    }

    private static async Task<IReadOnlyList<PersonShape>> ListAsync(HttpClient owner)
    {
        var response = await owner.GetAsync("/api/v1/people");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<IReadOnlyList<PersonShape>>())!;
    }

    internal static async Task ShouldBeInvalidInviteAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ErrorShape>())!.ErrorCode.Should().Be("INVALID_INVITE");
    }

    private sealed record InviteShape(string UserId, string Email, string Token, DateTime ExpiresAt);

    private sealed record PersonShape(string Id, string Email, IReadOnlyList<string> Roles, string Status);

    private sealed record UserShape(string Id, string Email, IReadOnlyList<string> Roles);

    private sealed record AuthResponseShape(UserShape User, DateTime ExpiresAt);

    private sealed record ErrorShape(string Error, string ErrorCode);
}

/// <summary>An invite token past its lifespan is refused (the lifespan is set to zero for this host).</summary>
public class PeopleInviteExpiryTests(PeopleInviteExpiryTests.ExpiredInviteApiFactory factory)
    : IClassFixture<PeopleInviteExpiryTests.ExpiredInviteApiFactory>
{
    [Fact]
    public async Task AcceptInvite_AfterTheTokenExpired_ReturnsInvalidInvite()
    {
        var (userId, token) = await factory.CreatePendingInviteAsync("friend-expired@test.com");

        var response = await factory.CookieClient().PostAsJsonAsync("/api/v1/auth/invite/accept",
            new { userId, token, password = "quiet lantern orchard" });

        await PeopleInviteContractTests.ShouldBeInvalidInviteAsync(response);
        using var scope = factory.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(userId);
        user!.PasswordHash.Should().BeNull("an expired invite must not set a password");
    }

    public sealed class ExpiredInviteApiFactory : AuthApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
                services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.Zero));
        }
    }
}
