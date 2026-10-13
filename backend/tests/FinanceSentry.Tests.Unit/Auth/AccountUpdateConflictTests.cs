namespace FinanceSentry.Tests.Unit.Auth;

using FinanceSentry.Modules.Auth.Application.Commands;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Domain.Exceptions;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Moq;
using Xunit;

/// <summary>
/// Profile and preferences writes share one <c>AspNetUsers</c> row under Identity's concurrency stamp, so a lost
/// race must surface as a 409 the SPA can roll back on, never as a 200 carrying values that were not stored.
/// </summary>
public class AccountUpdateConflictTests
{
    private static Mock<UserManager<ApplicationUser>> UserManagerUpdating(ApplicationUser user, IdentityResult result)
    {
        var manager = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        manager.Setup(m => m.FindByIdAsync(It.IsAny<string>())).ReturnsAsync(user);
        manager.Setup(m => m.UpdateAsync(user)).ReturnsAsync(result);
        return manager;
    }

    private static readonly IdentityResult ConcurrencyFailure = IdentityResult.Failed(new IdentityErrorDescriber().ConcurrencyFailure());

    [Fact]
    public async Task UpdatePreferences_WhenIdentityRejectsTheWrite_Returns409()
    {
        var user = new ApplicationUser();
        var handler = new UpdatePreferencesCommandHandler(UserManagerUpdating(user, ConcurrencyFailure).Object);
        var command = new UpdatePreferencesCommand(Guid.NewGuid(), new AppearanceDto(ThemeModes.Dark, "#112233", 0.5));

        var act = () => handler.Handle(command, CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<AccountUpdateConflictException>();
        thrown.Which.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task UpdatePreferences_WhenIdentitySavesTheWrite_ReturnsTheStoredAppearance()
    {
        var user = new ApplicationUser();
        var handler = new UpdatePreferencesCommandHandler(UserManagerUpdating(user, IdentityResult.Success).Object);
        var command = new UpdatePreferencesCommand(Guid.NewGuid(), new AppearanceDto(ThemeModes.Dark, "#AABBCC", 0.5));

        var result = await handler.Handle(command, CancellationToken.None);

        result.Appearance.Should().Be(new AppearanceDto(ThemeModes.Dark, "#aabbcc", 0.5));
    }

    [Fact]
    public async Task UpdateProfile_WhenIdentityRejectsTheWrite_Returns409()
    {
        var user = new ApplicationUser();
        var handler = new UpdateProfileCommandHandler(UserManagerUpdating(user, ConcurrencyFailure).Object);
        var command = new UpdateProfileCommand(Guid.NewGuid(), "Ada", "Lovelace", "USD", true, true, 100m, true, true, 4m, 3m);

        var act = () => handler.Handle(command, CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<AccountUpdateConflictException>();
        thrown.Which.StatusCode.Should().Be(409);
    }
}
