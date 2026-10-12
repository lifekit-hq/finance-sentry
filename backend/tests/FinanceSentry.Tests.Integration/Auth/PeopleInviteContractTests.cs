namespace FinanceSentry.Tests.Integration.Auth;

using System.Net;
using System.Net.Http.Json;
using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Auth.Application.Commands;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// Invite-only onboarding: an owner invites an email on the People page, which creates a passwordless Member
/// account; the invited person's first Logto sign-in with that verified email links to it. There is no invite
/// link, no token and no password step. Self-service registration no longer exists.
/// </summary>
public class PeopleInviteContractTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private const string Password = "TestPass123!";
    private const string NewPassword = "quiet lantern orchard";
    private const string Provider = "TestIdp";

    [Fact]
    public async Task CreateInvite_AsOwner_ListsThePersonAsInvited()
    {
        using var owner = await OwnerClientAsync("invite-owner-create@test.com");

        var response = await owner.PostAsJsonAsync("/api/v1/people/invites", new { email = "friend-create@test.com" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var invite = (await response.Content.ReadFromJsonAsync<InviteShape>())!;
        invite.Email.Should().Be("friend-create@test.com");

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
    public async Task InvitedPerson_SignsInThroughTheProviderWithoutAPasswordStep()
    {
        using var owner = await OwnerClientAsync("invite-owner-accept@test.com");
        var invite = await InviteAsync(owner, "friend-accept@test.com");

        AuthResult result;
        using (var scope = factory.Services.CreateScope())
        {
            result = await scope.ServiceProvider
                .GetRequiredService<ICommandHandler<ExternalLoginCommand, AuthResult>>()
                .Handle(new ExternalLoginCommand(Provider, "sub-friend-accept", "friend-accept@test.com", true), CancellationToken.None);
        }

        result.Response.User.Id.Should().Be(invite.UserId);
        result.Response.User.Roles.Should().Equal(AuthRoles.Member);
        using (var scope = factory.Services.CreateScope())
        {
            var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(invite.UserId);
            user!.PasswordHash.Should().BeNull("the invite sets no password");
        }

        (await ListAsync(owner)).Single(p => p.Email == "friend-accept@test.com").Status.Should().Be("Active");
    }

    [Fact]
    public async Task InviteAcceptEndpoint_IsRemoved()
    {
        var userId = await factory.CreatePendingInviteAsync("friend-no-accept@test.com");

        var response = await factory.CookieClient().PostAsJsonAsync("/api/v1/auth/invite/accept",
            new { userId, token = "any", password = NewPassword });

        // An unmapped route falls under the deny-all fallback policy.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.Contains("Set-Cookie").Should().BeFalse();
    }

    [Fact]
    public async Task CreateInvite_ForAPendingEmail_IsIdempotent()
    {
        using var owner = await OwnerClientAsync("invite-owner-reissue@test.com");
        var first = await InviteAsync(owner, "friend-reissue@test.com");
        var second = await InviteAsync(owner, "friend-reissue@test.com");

        second.UserId.Should().Be(first.UserId);
        (await ListAsync(owner)).Count(p => p.Email == "friend-reissue@test.com").Should().Be(1);
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

    private sealed record InviteShape(string UserId, string Email);

    private sealed record PersonShape(string Id, string Email, IReadOnlyList<string> Roles, string Status);

    private sealed record ErrorShape(string Error, string ErrorCode);
}
