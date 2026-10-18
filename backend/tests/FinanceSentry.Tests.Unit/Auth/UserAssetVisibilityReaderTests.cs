namespace FinanceSentry.Tests.Unit.Auth;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Auth.Application.Commands;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Moq;
using Xunit;

public class UserAssetVisibilityReaderTests
{
    private static Mock<UserManager<ApplicationUser>> Manager(ApplicationUser? user)
    {
        var manager = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        manager.Setup(m => m.FindByIdAsync(It.IsAny<string>())).ReturnsAsync(user);
        manager.Setup(m => m.UpdateAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);
        return manager;
    }

    [Fact]
    public async Task Get_ForANewUser_ReturnsTheDefaults()
    {
        var reader = new UserAssetVisibilityReader(Manager(new ApplicationUser()).Object);

        var result = await reader.GetAsync(Guid.NewGuid());

        result.Should().Be(new UserAssetVisibility(true, false, 1m));
    }

    [Fact]
    public async Task Get_ReturnsTheStoredChoices()
    {
        var user = new ApplicationUser { HideEmptyBalances = false, HideSmallBalances = true, SmallBalanceThreshold = 25m };
        var reader = new UserAssetVisibilityReader(Manager(user).Object);

        var result = await reader.GetAsync(Guid.NewGuid());

        result.Should().Be(new UserAssetVisibility(false, true, 25m));
    }

    [Fact]
    public async Task Get_ForAnUnknownUser_ReturnsNull()
    {
        var reader = new UserAssetVisibilityReader(Manager(null).Object);

        (await reader.GetAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task UpdateProfile_StoresTheChoices_AndLeavesThemAloneWhenOmitted()
    {
        var user = new ApplicationUser();
        var handler = new UpdateProfileCommandHandler(Manager(user).Object);

        var saved = await handler.Handle(
            new UpdateProfileCommand(Guid.NewGuid(), "A", "B", "USD", true, true, 100m, true, true, 0.04m, 0.05m,
                HideEmptyBalances: false, HideSmallBalances: true, SmallBalanceThreshold: 5m),
            CancellationToken.None);
        saved.Should().Match<UserProfileDto>(p => !p.HideEmptyBalances && p.HideSmallBalances && p.SmallBalanceThreshold == 5m);

        var untouched = await handler.Handle(
            new UpdateProfileCommand(Guid.NewGuid(), "A", "B", "USD", true, true, 100m, true, true, 0.04m, 0.05m),
            CancellationToken.None);
        untouched.Should().Match<UserProfileDto>(p => !p.HideEmptyBalances && p.HideSmallBalances && p.SmallBalanceThreshold == 5m);
    }
}
