namespace FinanceSentry.Modules.Auth.Domain.People;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Auth.Domain.Entities;
using Microsoft.AspNetCore.Identity;

/// <summary>Whether a person is the only Owner left: closing that account would leave nobody to manage the instance.</summary>
public static class LastOwner
{
    public static async Task<bool> IsAsync(UserManager<ApplicationUser> userManager, ApplicationUser user)
    {
        if (!await userManager.IsInRoleAsync(user, AuthRoles.Owner))
            return false;

        var owners = await userManager.GetUsersInRoleAsync(AuthRoles.Owner);
        return !owners.Any(o => o.Id != user.Id && !PersonStatus.IsRevoked(o));
    }
}
