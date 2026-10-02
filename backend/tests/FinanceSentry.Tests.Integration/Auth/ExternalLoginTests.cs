namespace FinanceSentry.Tests.Integration.Auth;

using System.Net;
using System.Net.Http.Json;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Auth.Application.Commands;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Domain.Exceptions;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// The provider-agnostic external sign-in (<see cref="ExternalLoginCommand"/>) and the switches that turn the
/// password and direct-Google endpoints off.
/// </summary>
public class ExternalLoginTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private const string Provider = "TestIdp";

    [Fact]
    public async Task InvitedAccount_IsRelinkedByVerifiedEmail()
    {
        var (userId, _) = await factory.CreatePendingInviteAsync("ext-invited@test.com");

        var result = await SignInAsync(new ExternalLoginCommand(Provider, "sub-invited", "ext-invited@test.com", true));

        result.Response.User.Id.Should().Be(userId);
        (await FindByLoginAsync("sub-invited"))!.Id.Should().Be(userId);
    }

    [Fact]
    public async Task UninvitedEmail_IsRefusedAndCreatesNoAccount()
    {
        var act = () => SignInAsync(new ExternalLoginCommand(Provider, "sub-stranger", "ext-stranger@test.com", true));

        await act.Should().ThrowAsync<AccountNotInvitedException>();
        (await FindByLoginAsync("sub-stranger")).Should().BeNull();
        using var scope = factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
            .FindByEmailAsync("ext-stranger@test.com")).Should().BeNull();
    }

    [Fact]
    public async Task LockedOutAccount_IsRefusedAndLinksNothing()
    {
        await factory.EnsureUserExistsAsync("ext-locked@test.com", "TestPass123!");
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            await users.SetLockoutEndDateAsync((await users.FindByEmailAsync("ext-locked@test.com"))!, DateTimeOffset.UtcNow.AddHours(1));
        }

        var act = () => SignInAsync(new ExternalLoginCommand(Provider, "sub-locked", "ext-locked@test.com", true));

        await act.Should().ThrowAsync<InvalidCredentialsException>();
        (await FindByLoginAsync("sub-locked")).Should().BeNull();
    }

    [Fact]
    public async Task ExistingLogin_IsReusedEvenIfTheEmailChanged()
    {
        await factory.EnsureUserExistsAsync("ext-existing@test.com", "TestPass123!");
        var first = await SignInAsync(new ExternalLoginCommand(Provider, "sub-existing", "ext-existing@test.com", true));

        // Resolved by (provider, subject): the email, verified or not, is not consulted again.
        var second = await SignInAsync(new ExternalLoginCommand(Provider, "sub-existing", "someone-else@test.com", false));

        second.Response.User.Id.Should().Be(first.Response.User.Id);
    }

    [Fact]
    public async Task UnverifiedEmail_DoesNotRelinkAnExistingAccount()
    {
        await factory.EnsureUserExistsAsync("ext-unverified@test.com", "TestPass123!");

        var act = () => SignInAsync(new ExternalLoginCommand(Provider, "sub-unverified", "ext-unverified@test.com", false));

        await act.Should().ThrowAsync<AccountNotInvitedException>();
        (await FindByLoginAsync("sub-unverified")).Should().BeNull();
    }

    [Fact]
    public async Task PasswordLoginDisabled_LoginReturns403AndGoogleStillWorks()
    {
        await factory.EnsureUserExistsAsync("google@test.com", "TestPass123!");
        using var disabled = factory.WithWebHostBuilder(b => b.UseSetting("Auth:PasswordLogin:Enabled", "false"));
        var client = disabled.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var login = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "google@test.com", password = "TestPass123!" });
        var google = await client.PostAsJsonAsync("/api/v1/auth/google/verify",
            new { credential = "valid-test-credential" });

        login.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await login.Content.ReadFromJsonAsync<ErrorShape>())!.ErrorCode.Should().Be("SIGN_IN_METHOD_DISABLED");
        google.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GoogleDirectDisabled_GoogleVerifyReturns403AndPasswordLoginStillWorks()
    {
        await factory.EnsureUserExistsAsync("google@test.com", "TestPass123!");
        using var disabled = factory.WithWebHostBuilder(b => b.UseSetting("Auth:GoogleDirect:Enabled", "false"));
        var client = disabled.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var google = await client.PostAsJsonAsync("/api/v1/auth/google/verify",
            new { credential = "valid-test-credential" });
        var login = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "google@test.com", password = "TestPass123!" });

        google.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await google.Content.ReadFromJsonAsync<ErrorShape>())!.ErrorCode.Should().Be("SIGN_IN_METHOD_DISABLED");
        login.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<AuthResult> SignInAsync(ExternalLoginCommand command)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider
            .GetRequiredService<ICommandHandler<ExternalLoginCommand, AuthResult>>()
            .Handle(command, CancellationToken.None);
    }

    private async Task<ApplicationUser?> FindByLoginAsync(string subject)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByLoginAsync(Provider, subject);
    }

    private record ErrorShape(string Error, string ErrorCode);
}
