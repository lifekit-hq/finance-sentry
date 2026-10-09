namespace FinanceSentry.Modules.Auth.Infrastructure.Authorization;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;

/// <summary>A login of the <see cref="OidcLoginOptions.Scheme"/> provider in Identity's login table means the account is linked.</summary>
public sealed class OrgIdentityLinkReader(UserManager<ApplicationUser> users) : IOrgIdentityLinkReader
{
    public async Task<bool> IsLinkedAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null)
            return false;

        return (await users.GetLoginsAsync(user)).Any(l => l.LoginProvider == OidcLoginOptions.Scheme);
    }
}
