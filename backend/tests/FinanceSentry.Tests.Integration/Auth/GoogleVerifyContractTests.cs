namespace FinanceSentry.Tests.Integration.Auth;

using System.Net;
using System.Net.Http.Json;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// REST API contract tests for POST /api/v1/auth/google/verify: Google sign-in works for existing accounts only,
/// resolved and linked through Identity's external-login table; it never creates an account.
/// </summary>
public class GoogleVerifyContractTests : IClassFixture<AuthApiFactory>
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public GoogleVerifyContractTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task GoogleVerify_ValidCredential_Returns200WithAuthResponseSchema()
    {
        await _factory.EnsureUserExistsAsync("google@test.com", "TestPass123!");

        var response = await _client.PostAsJsonAsync("/api/v1/auth/google/verify",
            new { credential = "valid-test-credential" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<AuthResponseShape>();
        body.Should().NotBeNull();
        body!.User.Should().NotBeNull();
        body.User.Id.Should().NotBeNullOrWhiteSpace();
        Guid.TryParse(body.User.Id, out _).Should().BeTrue("UserId must be a valid GUID");
        body.ExpiresAt.Should().BeAfter(DateTime.UtcNow);

        response.Headers.TryGetValues("Set-Cookie", out var cookies);
        cookies.Should().Contain(c => c.StartsWith("fs_access_token="), "google verify must set the access token cookie");
    }

    [Fact]
    public async Task GoogleVerify_EmptyCredential_Returns400WithValidationError()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/google/verify",
            new { credential = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadFromJsonAsync<ErrorResponseShape>();
        body.Should().NotBeNull();
        body!.ErrorCode.Should().Be("VALIDATION_ERROR");
    }

    [Fact]
    public async Task GoogleVerify_InvalidCredential_Returns400WithInvalidGoogleCredential()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/google/verify",
            new { credential = "invalid-credential" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadFromJsonAsync<ErrorResponseShape>();
        body.Should().NotBeNull();
        body!.ErrorCode.Should().Be("INVALID_GOOGLE_CREDENTIAL");
    }

    [Fact]
    public async Task GoogleVerify_UnknownEmail_Returns403AndCreatesNoAccount()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/google/verify",
            new { credential = "new-user-credential" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponseShape>();
        body!.ErrorCode.Should().Be("ACCOUNT_NOT_INVITED");

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        (await userManager.FindByEmailAsync("newgoogle@test.com")).Should().BeNull("Google sign-in must not create accounts");
        (await userManager.FindByLoginAsync("Google", "google-sub-new")).Should().BeNull();
    }

    [Fact]
    public async Task GoogleVerify_ExistingGoogleUser_Returns200WithSameUserId()
    {
        await _factory.EnsureUserExistsAsync("google@test.com", "TestPass123!");

        // First call links the Google account to the existing one
        var first = await _client.PostAsJsonAsync("/api/v1/auth/google/verify",
            new { credential = "valid-test-credential" });
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBody = await first.Content.ReadFromJsonAsync<AuthResponseShape>();

        // Second call resolves through the external login
        var second = await _client.PostAsJsonAsync("/api/v1/auth/google/verify",
            new { credential = "valid-test-credential" });
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondBody = await second.Content.ReadFromJsonAsync<AuthResponseShape>();

        secondBody!.User.Id.Should().Be(firstBody!.User.Id, "same Google account must always resolve to same user");
    }

    [Fact]
    public async Task GoogleVerify_EmailMatchesExistingAccount_Returns200AndLinksExternalLogin()
    {
        await _factory.EnsureUserExistsAsync("link@test.com", "TestPass123!");

        var response = await _client.PostAsJsonAsync("/api/v1/auth/google/verify",
            new { credential = "link-credential" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync("link@test.com");
        var linked = await userManager.FindByLoginAsync("Google", "google-sub-link");
        linked.Should().NotBeNull("the Google account must be linked in the external-login table");
        linked!.Id.Should().Be(user!.Id);
        user.GoogleId.Should().BeNull("the legacy column is no longer written");
    }

    [Fact]
    public async Task GoogleVerify_PendingInvite_Returns200AndActivatesTheInvitedAccount()
    {
        var (userId, _) = await _factory.CreatePendingInviteAsync("invited-google@test.com");

        var response = await _client.PostAsJsonAsync("/api/v1/auth/google/verify",
            new { credential = "invited-credential" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<AuthResponseShape>())!.User.Id.Should().Be(userId);
    }

    [Fact]
    public async Task GoogleVerify_LockedOutAccount_Returns401AndLinksNothing()
    {
        await _factory.EnsureUserExistsAsync("revoked-google@test.com", "TestPass123!");
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByEmailAsync("revoked-google@test.com");
            await users.SetLockoutEndDateAsync(user!, DateTimeOffset.UtcNow.AddHours(1));
        }

        var response = await _client.PostAsJsonAsync("/api/v1/auth/google/verify",
            new { credential = "revoked-credential" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var check = _factory.Services.CreateScope();
        (await check.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
            .FindByLoginAsync("Google", "google-sub-revoked")).Should().BeNull();
    }

    private record UserShape(string Id, string Email);
    private record AuthResponseShape(UserShape User, DateTime ExpiresAt);
    private record ErrorResponseShape(string Error, string ErrorCode);
}
