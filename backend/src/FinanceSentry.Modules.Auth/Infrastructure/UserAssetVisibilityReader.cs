namespace FinanceSentry.Modules.Auth.Infrastructure;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Entities;
using Microsoft.AspNetCore.Identity;

public class UserAssetVisibilityReader(UserManager<ApplicationUser> users) : IUserAssetVisibilityReader
{
    public async Task<UserAssetVisibility?> GetAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null) return null;
        return new UserAssetVisibility(user.HideEmptyBalances, user.HideSmallBalances, user.SmallBalanceThreshold);
    }
}
