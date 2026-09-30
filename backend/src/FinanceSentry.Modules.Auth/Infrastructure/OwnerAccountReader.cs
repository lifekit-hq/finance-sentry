namespace FinanceSentry.Modules.Auth.Infrastructure;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Entities;
using Microsoft.AspNetCore.Identity;

public class OwnerAccountReader(UserManager<ApplicationUser> users) : IOwnerAccountReader
{
    public async Task<bool> IsOwnerAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        return user is not null && await users.IsInRoleAsync(user, AuthRoles.Owner);
    }
}
