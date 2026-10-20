namespace FinanceSentry.Tests.Integration.Auth;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Auth.Application.Commands;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Domain.Exceptions;
using FinanceSentry.Modules.Auth.Domain.People;
using FinanceSentry.Modules.Auth.Infrastructure.Authorization;
using FinanceSentry.Modules.Auth.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

/// <summary>
/// The provider-agnostic external sign-in (<see cref="ExternalLoginCommand"/>), which is how every account
/// signs in now that the password endpoint is a break-glass path behind a flag.
/// </summary>
public class ExternalLoginTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private const string Provider = "TestIdp";

    [Fact]
    public async Task InvitedAccount_IsRelinkedByVerifiedEmail()
    {
        var userId = await factory.CreatePendingInviteAsync("ext-invited@test.com");

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
    public async Task AccountWithOnlyTheRetiredDirectGoogleLogin_IsRelinkedByVerifiedEmail()
    {
        // The retired direct-Google path left a "Google" row in AspNetUserLogins and no other login.
        await factory.EnsureUserExistsAsync("ext-google-only@test.com", "TestPass123!");
        string userId;
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByEmailAsync("ext-google-only@test.com"))!;
            userId = user.Id;
            (await users.AddLoginAsync(user, new UserLoginInfo("Google", "google-sub-friend", "Google"))).Succeeded.Should().BeTrue();
        }

        var result = await SignInAsync(new ExternalLoginCommand(Provider, "sub-google-only", "ext-google-only@test.com", true));

        result.Response.User.Id.ToString().Should().Be(userId);
        using var verify = factory.Services.CreateScope();
        var verifyUsers = verify.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var logins = await verifyUsers.GetLoginsAsync((await verifyUsers.FindByIdAsync(userId))!);
        logins.Select(l => l.LoginProvider).Should().BeEquivalentTo("Google", Provider);
    }

    [Fact]
    public async Task AccountWithOnlyTheRetiredDirectGoogleLogin_IsNotRelinkedByUnverifiedEmail()
    {
        await factory.EnsureUserExistsAsync("ext-google-unverified@test.com", "TestPass123!");
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            await users.AddLoginAsync((await users.FindByEmailAsync("ext-google-unverified@test.com"))!,
                new UserLoginInfo("Google", "google-sub-unverified", "Google"));
        }

        var act = () => SignInAsync(new ExternalLoginCommand(Provider, "sub-google-unverified", "ext-google-unverified@test.com", false));

        await act.Should().ThrowAsync<AccountNotInvitedException>();
        (await FindByLoginAsync("sub-google-unverified")).Should().BeNull();
    }

    [Fact]
    public async Task SeededPasswordlessSmokeAccount_IsLinkedByTheProviderUsersVerifiedEmail()
    {
        // What the post-deploy smoke relies on: the startup seed makes an account with no password, and the first
        // provider sign-in as the dedicated provider user (same verified email) links it.
        Guid? seeded;
        using (var scope = factory.Services.CreateScope())
        {
            var options = new AuthSeedOptions { SmokeAccount = new SmokeAccountOptions { Email = "ext-smoke@test.com" } };
            seeded = await SmokeAccountSeeder.SeedAsync(
                scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(), options, NullLogger.Instance);
        }

        var result = await SignInAsync(new ExternalLoginCommand(Provider, "sub-smoke", "ext-smoke@test.com", true));

        result.Response.User.Id.Should().Be(seeded!.Value.ToString());
        (await FindByLoginAsync("sub-smoke"))!.Id.Should().Be(seeded.Value.ToString());
    }

    [Fact]
    public async Task InviteeWithNoPassword_SignsInThroughTheProviderAndAnUninvitedEmailIsRefused()
    {
        var userId = await factory.CreatePendingInviteAsync("pw-off-invitee@test.com");

        var result = await SignInAsync(new ExternalLoginCommand(Provider, "sub-pw-off-invitee", "pw-off-invitee@test.com", true));

        result.Response.User.Id.Should().Be(userId);
        (await FindByLoginAsync("sub-pw-off-invitee"))!.Id.Should().Be(userId);
        using (var scope = factory.Services.CreateScope())
        {
            var invitee = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(userId);
            invitee!.PasswordHash.Should().BeNull();
        }

        var uninvited = async () => await SignInAsync(new ExternalLoginCommand(Provider, "sub-pw-off-stranger", "pw-off-stranger@test.com", true));
        await uninvited.Should().ThrowAsync<AccountNotInvitedException>();
    }

    [Fact]
    public async Task RoleHolderWithNoAccount_GetsAnActiveMemberAccountLinkedToTheProvider()
    {
        var result = await SignInAsync(new ExternalLoginCommand(Provider, "sub-granted", " ext-granted@test.com ", true, HasAppRole: true));

        result.Response.User.Email.Should().Be("ext-granted@test.com");
        result.Response.User.Roles.Should().Equal(AuthRoles.Member);
        result.Response.User.Permissions.Should().BeEquivalentTo(Permissions.MemberDefaults);
        var user = (await FindByLoginAsync("sub-granted"))!;
        user.Id.Should().Be(result.Response.User.Id);
        user.PasswordHash.Should().BeNull();
        PersonStatus.Of(user, hasExternalLogin: true).Should().Be(PersonStatus.Active);
    }

    [Fact]
    public async Task RoleHolder_SigningInAgain_KeepsTheAccountTheFirstSignInCreated()
    {
        var first = await SignInAsync(new ExternalLoginCommand(Provider, "sub-granted-again", "ext-granted-again@test.com", true, HasAppRole: true));
        var second = await SignInAsync(new ExternalLoginCommand(Provider, "sub-granted-again", "ext-granted-again@test.com", true, HasAppRole: true));

        second.Response.User.Id.Should().Be(first.Response.User.Id);
    }

    [Fact]
    public async Task RoleHolderWithAnUnverifiedEmail_IsRefusedAndCreatesNoAccount()
    {
        var act = () => SignInAsync(new ExternalLoginCommand(Provider, "sub-granted-unverified", "ext-granted-unverified@test.com", false, HasAppRole: true));

        await act.Should().ThrowAsync<AccountNotInvitedException>();
        using var scope = factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
            .FindByEmailAsync("ext-granted-unverified@test.com")).Should().BeNull();
    }

    [Fact]
    public async Task RevokedAccount_StaysRefusedWithTheRole()
    {
        // Linked first, so both the login match and the email match are covered.
        var userId = await factory.CreatePendingInviteAsync("ext-revoked@test.com");
        await SignInAsync(new ExternalLoginCommand(Provider, "sub-revoked", "ext-revoked@test.com", true));
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ICommandHandler<RevokePersonCommand, Unit>>()
                .Handle(new RevokePersonCommand(userId, Guid.NewGuid().ToString()), CancellationToken.None);
        }

        var linked = () => SignInAsync(new ExternalLoginCommand(Provider, "sub-revoked", "ext-revoked@test.com", true, HasAppRole: true));
        var byEmail = () => SignInAsync(new ExternalLoginCommand(Provider, "sub-revoked-other", "ext-revoked@test.com", true, HasAppRole: true));

        await linked.Should().ThrowAsync<InvalidCredentialsException>();
        await byEmail.Should().ThrowAsync<InvalidCredentialsException>();
        (await FindByLoginAsync("sub-revoked-other")).Should().BeNull();
    }

    [Fact]
    public async Task ClosedAccount_StaysRefusedWithTheRoleAndNoSecondAccountIsMade()
    {
        // A closed account as the closure leaves it: locked out for good, unlinked, with a pending closure record.
        var userId = await factory.CreatePendingInviteAsync("ext-closed@test.com");
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByIdAsync(userId))!;
            await users.SetLockoutEnabledAsync(user, true);
            await users.SetLockoutEndDateAsync(user, PersonStatus.RevokedLockoutEnd);
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            db.AccountClosures.Add(new AccountClosure(Guid.Parse(userId), DateTime.UtcNow));
            await db.SaveChangesAsync();
        }

        var act = () => SignInAsync(new ExternalLoginCommand(Provider, "sub-closed", "ext-closed@test.com", true, HasAppRole: true));

        await act.Should().ThrowAsync<InvalidCredentialsException>();
        (await FindByLoginAsync("sub-closed")).Should().BeNull();
        using var check = factory.Services.CreateScope();
        check.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().Users
            .Count(u => u.Email == "ext-closed@test.com").Should().Be(1);
    }

    [Fact]
    public async Task ExistingAccountWithoutTheRole_SignsInAsBefore()
    {
        // The owner and current members may not hold the provider role yet; the role is only consulted to create.
        await factory.EnsureUserExistsAsync("ext-no-role@test.com", "TestPass123!");
        var first = await SignInAsync(new ExternalLoginCommand(Provider, "sub-no-role", "ext-no-role@test.com", true, HasAppRole: false));

        var again = await SignInAsync(new ExternalLoginCommand(Provider, "sub-no-role", "ext-no-role@test.com", true, HasAppRole: false));

        again.Response.User.Id.Should().Be(first.Response.User.Id);
        again.Response.User.Roles.Should().Equal(AuthRoles.Member);
        (await FindByLoginAsync("sub-no-role"))!.Id.Should().Be(first.Response.User.Id);
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
