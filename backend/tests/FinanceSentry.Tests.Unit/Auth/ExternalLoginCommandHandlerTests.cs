namespace FinanceSentry.Tests.Unit.Auth;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Auth.Application.Commands;
using FinanceSentry.Modules.Auth.Application.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Domain.Exceptions;
using FinanceSentry.Modules.Auth.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Moq;
using Xunit;

/// <summary>
/// The provider's app role is the invite: it lets a verified first sign-in with no account create a Member, and it is
/// never consulted for an account that already exists, so nobody who signs in today loses access without it.
/// </summary>
public class ExternalLoginCommandHandlerTests
{
    private const string Provider = "TestIdp";
    private const string Email = "person@test.com";

    private readonly Mock<UserManager<ApplicationUser>> _users = new(
        Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
    private readonly Mock<IMemberAccountCreator> _memberAccounts = new();

    public ExternalLoginCommandHandlerTests()
    {
        _users.Setup(m => m.AddLoginAsync(It.IsAny<ApplicationUser>(), It.IsAny<UserLoginInfo>())).ReturnsAsync(IdentityResult.Success);
        _memberAccounts.Setup(m => m.CreateAsync(Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApplicationUser { Id = "created", UserName = Email, Email = Email });
    }

    private ExternalLoginCommandHandler Handler()
    {
        var access = new Mock<IUserAccessService>();
        access.Setup(a => a.GetAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(new UserAccess([AuthRoles.Member], Permissions.MemberDefaults));
        var tokens = new Mock<ITokenService>();
        tokens.Setup(t => t.GenerateToken(It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<string>>()))
            .Returns(("access", DateTime.UtcNow.AddHours(1)));
        var refresh = new Mock<IRefreshTokenService>();
        refresh.Setup(r => r.IssueAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(("refresh", new RefreshToken("user", "hash", DateTime.UtcNow.AddDays(1))));
        return new ExternalLoginCommandHandler(_users.Object, access.Object, _memberAccounts.Object, tokens.Object, refresh.Object);
    }

    [Fact]
    public async Task NoAccount_WithTheRole_CreatesAMemberAndLinksIt()
    {
        var result = await Handler().Handle(new ExternalLoginCommand(Provider, "sub", Email, true, HasAppRole: true), CancellationToken.None);

        result.Response.User.Id.Should().Be("created");
        _users.Verify(m => m.AddLoginAsync(It.Is<ApplicationUser>(u => u.Id == "created"),
            It.Is<UserLoginInfo>(l => l.LoginProvider == Provider && l.ProviderKey == "sub")), Times.Once);
    }

    [Fact]
    public async Task NoAccount_WithoutTheRole_IsRefused()
    {
        var act = () => Handler().Handle(new ExternalLoginCommand(Provider, "sub", Email, true), CancellationToken.None);

        await act.Should().ThrowAsync<AccountNotInvitedException>();
        _memberAccounts.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UnverifiedEmail_WithTheRole_IsRefused()
    {
        var act = () => Handler().Handle(new ExternalLoginCommand(Provider, "sub", Email, false, HasAppRole: true), CancellationToken.None);

        await act.Should().ThrowAsync<AccountNotInvitedException>();
        _users.Verify(m => m.FindByEmailAsync(It.IsAny<string>()), Times.Never);
        _memberAccounts.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LockedOutAccount_WithTheRole_IsRefusedAndNoAccountIsCreated()
    {
        var revoked = new ApplicationUser { Id = "revoked", Email = Email };
        _users.Setup(m => m.FindByEmailAsync(Email)).ReturnsAsync(revoked);
        _users.Setup(m => m.IsLockedOutAsync(revoked)).ReturnsAsync(true);

        var act = () => Handler().Handle(new ExternalLoginCommand(Provider, "sub", Email, true, HasAppRole: true), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidCredentialsException>();
        _memberAccounts.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExistingAccount_WithoutTheRole_SignsIn()
    {
        var existing = new ApplicationUser { Id = "existing", Email = Email };
        _users.Setup(m => m.FindByLoginAsync(Provider, "sub")).ReturnsAsync(existing);

        var result = await Handler().Handle(new ExternalLoginCommand(Provider, "sub", Email, true), CancellationToken.None);

        result.Response.User.Id.Should().Be("existing");
        _memberAccounts.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("finance-sentry", new[] { "other", "finance-sentry" }, true)]
    [InlineData("finance-sentry", new[] { "Finance-Sentry" }, false)]
    [InlineData("finance-sentry", new string[0], false)]
    [InlineData("", new[] { "" }, false)]
    public void GrantsApp_MatchesOnlyTheConfiguredRole(string requiredRole, string[] roles, bool expected) =>
        new OidcLoginOptions { RequiredRole = requiredRole }.GrantsApp(roles).Should().Be(expected);
}
