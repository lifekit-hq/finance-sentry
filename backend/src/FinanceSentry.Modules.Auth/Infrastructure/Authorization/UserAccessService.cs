namespace FinanceSentry.Modules.Auth.Infrastructure.Authorization;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Auth.Application.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Entities;
using Microsoft.AspNetCore.Identity;

public sealed class UserAccessService(
    UserManager<ApplicationUser> users,
    RoleManager<IdentityRole> roles,
    IUserClaimsPrincipalFactory<ApplicationUser> principalFactory) : IUserAccessService
{
    public async Task<UserAccess> GetAsync(ApplicationUser user)
    {
        var userRoles = await users.GetRolesAsync(user);
        var principal = await principalFactory.CreateAsync(user);
        var permissions = principal.FindAll(Permissions.ClaimType)
            .Select(c => c.Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        return new UserAccess(userRoles.ToList(), permissions);
    }

    public async Task GrantDefaultRoleAsync(ApplicationUser user)
    {
        if (!await roles.RoleExistsAsync(AuthRoles.Member))
            return;

        var result = await users.AddToRoleAsync(user, AuthRoles.Member);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Failed to grant the Member role: {string.Join(", ", result.Errors.Select(e => e.Description))}");
    }
}
